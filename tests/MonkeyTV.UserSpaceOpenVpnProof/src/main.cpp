#include <winsock2.h>
#include <windows.h>
#include <wincred.h>
#include <iphlpapi.h>
#include <objbase.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <deque>
#include <fstream>
#include <iostream>
#include <memory>
#include <mutex>
#include <regex>
#include <sstream>
#include <string>
#include <thread>
#include <vector>

#define OPENVPN_LOG(x) do { } while (false)
#define OPENVPN_LOG_NTNL(x) do { } while (false)
#include <client/ovpncli.hpp>
#include <openvpn/time/asiotimer.hpp>
#include <openvpn/tun/builder/base.hpp>
#include <openvpn/tun/client/tunprop.hpp>
#include <openvpn/tun/extern/config.hpp>

// OpenVPN establishes the native Windows ssize_t and Winsock declarations first.
// Prevent lwIP's portable compatibility declarations from redefining either.
#ifndef SSIZE_MAX
#define SSIZE_MAX INTPTR_MAX
#endif
#define LWIP_NO_UNISTD_H 1
extern "C" {
#include <lwip/dns.h>
#include <lwip/init.h>
#include <lwip/ip4.h>
#include <lwip/netif.h>
#include <lwip/pbuf.h>
#include <lwip/tcp.h>
#include <lwip/timeouts.h>
}

extern "C" u32_t sys_now(void) {
    return static_cast<u32_t>(GetTickCount64());
}

namespace proof {
bool g_proxyMode{};
bool g_proxyDiagnostics{};
std::string g_expectedCountry{"CA"};
constexpr char kHost[] = "ip-api.com";
constexpr char kRequest[] =
    "GET /line/?fields=countryCode HTTP/1.1\r\n"
    "Host: ip-api.com\r\n"
    "User-Agent: MonkeyTV-Disposable-Proof\r\n"
    "Connection: keep-alive\r\n\r\n";

struct CountryProbe {
    const char* host;
    const char* path;
};

// Country readiness deliberately uses independent public datasets.  A single
// provider can have stale VPN geolocation data, which previously let Monkey TV
// advertise the requested country even when a browser-visible checker disagreed.
constexpr std::array<CountryProbe, 3> kCountryProbes{{
    {"ip-api.com", "/line/?fields=countryCode"},
    {"ipwho.is", "/"},
    {"ifconfig.co", "/country-iso"},
}};

struct Evidence {
    std::mutex mutex;
    bool openVpnConnected{};
    bool externalTunnelReady{};
    bool firstLog{};
    bool firstEvent{};
    bool socketProtect{};
    bool tunFactory{};
    bool connectWorkerStarted{};
    bool outboundBridge{};
    bool inboundBridge{};
    bool dnsPacketToTunnel{};
    bool dnsResolved{};
    bool tcpConnected{};
    bool proxyReady{};
    bool canada{};
    bool failClosed{};
    bool completed{};
    unsigned long windowsDestinationTcpSockets{};
    std::string error;
    std::vector<std::string> stages;
} g;

std::string Timestamp() {
    SYSTEMTIME now{};
    GetSystemTime(&now);
    char value[32]{};
    sprintf_s(value, "%02u:%02u:%02u.%03uZ", now.wHour, now.wMinute,
              now.wSecond, now.wMilliseconds);
    return value;
}

std::string SanitizeFailure(std::string text) {
    if (text.size() > 240) text.resize(240);
    text = std::regex_replace(text, std::regex(R"(\b(?:\d{1,3}\.){3}\d{1,3}\b)"), "[ip]");
    text = std::regex_replace(text, std::regex(R"([A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+)"), "[identity]");
    text = std::regex_replace(text,
        std::regex(R"((password|username|token|credential)\s*[:=]\s*\S+)",
                   std::regex_constants::icase),
        "$1=[redacted]");
    return text;
}

void Stage(const std::string& name, bool error = false, const std::string& detail = {}) {
    std::ostringstream line;
    line << Timestamp() << " " << SanitizeFailure(name) << " error=" << (error ? 1 : 0);
    if (error && !detail.empty()) line << " detail=" << SanitizeFailure(detail);
    const auto safeLine = line.str();
    {
        std::lock_guard lock(g.mutex);
        g.stages.push_back(safeLine);
    }
    // App integration consumes only this safe lifecycle stream. It never includes
    // credentials, full configuration text, or public IP addresses.
    if (g_proxyDiagnostics) std::cerr << "VPNV2_STAGE " << safeLine << std::endl;
}

class ProofClient;
ProofClient* g_client{};
void Finish(bool pass, std::string reason);

unsigned long CountProcessRemoteTcpSockets() {
    DWORD size{};
    GetExtendedTcpTable(nullptr, &size, FALSE, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
    std::vector<unsigned char> storage(size);
    if (GetExtendedTcpTable(storage.data(), &size, FALSE, AF_INET,
                            TCP_TABLE_OWNER_PID_ALL, 0) != NO_ERROR) return ULONG_MAX;
    auto* table = reinterpret_cast<MIB_TCPTABLE_OWNER_PID*>(storage.data());
    unsigned long count{};
    const DWORD pid = GetCurrentProcessId();
    for (DWORD i = 0; i < table->dwNumEntries; ++i) {
        const auto& row = table->table[i];
        const DWORD hostAddress = ntohl(row.dwRemoteAddr);
        const bool loopback = (hostAddress >> 24) == 127;
        const bool active = row.dwState == MIB_TCP_STATE_SYN_SENT ||
                            row.dwState == MIB_TCP_STATE_SYN_RCVD ||
                            row.dwState == MIB_TCP_STATE_ESTAB ||
                            row.dwState == MIB_TCP_STATE_FIN_WAIT1 ||
                            row.dwState == MIB_TCP_STATE_FIN_WAIT2 ||
                            row.dwState == MIB_TCP_STATE_CLOSE_WAIT ||
                            row.dwState == MIB_TCP_STATE_CLOSING ||
                            row.dwState == MIB_TCP_STATE_LAST_ACK;
        if (row.dwOwningPid == pid && row.dwRemoteAddr != 0 && !loopback && active) ++count;
    }
    return count;
}

class PacketClient;
class PacketFactory final : public openvpn::TunClientFactory {
public:
    explicit PacketFactory(openvpn::ExternalTun::Config config) : config_(std::move(config)) {}
    openvpn::TunClient::Ptr new_tun_client_obj(openvpn_io::io_context&,
        openvpn::TunClientParent&, openvpn::TransportClient*) override;
    bool supports_epoch_data() override { return true; }
    const openvpn::ExternalTun::Config& config() const { return config_; }
private:
    openvpn::ExternalTun::Config config_;
};

class PacketClient final : public openvpn::TunClient, public openvpn::TunBuilderBase {
public:
    PacketClient(openvpn_io::io_context& io, PacketFactory* factory,
                 openvpn::TunClientParent& parent)
        : timer_(io), factory_(factory), parent_(parent) {}
    ~PacketClient() override { Cleanup(); }

    void tun_start(const openvpn::OptionList& options, openvpn::TransportClient& transport,
                   openvpn::CryptoDCSettings&) override {
        Stage("EXTERNAL_TUNNEL_ACTIVATION_START");
        state_ = openvpn::TunProp::State::Ptr(new openvpn::TunProp::State());
        parent_.tun_pre_tun_config();
        openvpn::TunProp::configure_builder(this, state_.get(), factory_->config().stats.get(),
            transport.server_endpoint_addr(), factory_->config().tun_prop, options, nullptr, false);
        if (address_.empty()) throw std::runtime_error("Nord did not assign IPv4");
        InitLwip();
        parent_.tun_connected();
        {
            std::lock_guard lock(g.mutex);
            g.openVpnConnected = true;
            g.externalTunnelReady = true;
        }
        Stage("EXTERNAL_TUNNEL_READY");
        if (g_proxyMode) { StartProxy(); StartReadinessProbe(); } else StartDns();
        Tick();
    }

    bool tun_send(openvpn::BufferAllocated& buffer) override {
        if (stopped_ || !ready_ || buffer.empty()) return false;
        pbuf* packet = pbuf_alloc(PBUF_RAW, static_cast<u16_t>(buffer.size()), PBUF_RAM);
        if (!packet || pbuf_take(packet, buffer.c_data(), static_cast<u16_t>(buffer.size())) != ERR_OK) {
            if (packet) pbuf_free(packet);
            Finish(false, "lwIP inbound allocation/copy failed");
            return false;
        }
        {
            std::lock_guard lock(g.mutex);
            g.inboundBridge = true;
        }
        ip4_input(packet, &netif_); // lwIP owns packet after input.
        return true;
    }

    void stop() override { Cleanup(); }
    void set_disconnect() override { tunnelUp_ = false; }
    std::string tun_name() const override { return "MonkeyTVUserSpace"; }
    std::string vpn_ip4() const override { return address_; }
    std::string vpn_ip6() const override { return {}; }
    int vpn_mtu() const override { return mtu_; }

    bool tun_builder_new() override { return true; }
    bool tun_builder_set_remote_address(const std::string&, bool) override { return true; }
    bool tun_builder_add_address(const std::string& address, int prefix, const std::string& gateway,
                                 bool ipv6, bool) override {
        if (!ipv6) { address_ = address; prefix_ = prefix; gateway_ = gateway; }
        return true;
    }
    bool tun_builder_add_route(const std::string&, int, int, bool) override { return true; }
    bool tun_builder_exclude_route(const std::string&, int, int, bool) override { return true; }
    bool tun_builder_reroute_gw(bool, bool, unsigned int) override { return true; }
    bool tun_builder_set_mtu(int mtu) override { mtu_ = std::min(mtu, 1400); return true; }
    bool tun_builder_set_session_name(const std::string&) override { return true; }
    bool tun_builder_set_dns_options(const openvpn::DnsOptions&) override { return true; }

private:
    struct Flow {
        PacketClient* owner{};
        SOCKET browser{INVALID_SOCKET};
        tcp_pcb* pcb{};
        std::string host;
        uint16_t port{};
        std::atomic<bool> connected{};
        std::atomic<bool> closing{};
        std::atomic<bool> browserClosed{};
        std::mutex inputMutex;
        std::deque<std::vector<unsigned char>> input;
        std::thread reader;
    };
    struct PendingConnect { SOCKET browser; std::string host; uint16_t port; };

    static err_t InterfaceInit(netif* iface) {
        auto* self = static_cast<PacketClient*>(iface->state);
        iface->name[0] = 'm'; iface->name[1] = 't';
        iface->output = &PacketClient::Output;
        iface->mtu = static_cast<u16_t>(self->mtu_);
        iface->flags = NETIF_FLAG_UP | NETIF_FLAG_LINK_UP;
        return ERR_OK;
    }
    static err_t Output(netif* iface, pbuf* packet, const ip4_addr_t* destination) {
        return static_cast<PacketClient*>(iface->state)->SendPacket(packet, destination);
    }
    err_t SendPacket(pbuf* packet, const ip4_addr_t* destination) {
        if (!tunnelUp_ || stopped_) {
            Stage("LWIP_OUTPUT_REJECTED_TUNNEL_NOT_READY", true);
            return ERR_RTE;
        }
        std::vector<unsigned char> bytes(packet->tot_len);
        pbuf_copy_partial(packet, bytes.data(), packet->tot_len, 0);
        // OpenVPN owns the transport framing.  External TUN packets must reserve
        // the negotiated READ_TUN headroom before being passed back to it, exactly
        // like the native Wintun client does.
        openvpn::BufferAllocated output;
        factory_->config().frame->prepare(openvpn::Frame::READ_TUN, output);
        output.write(bytes.data(), bytes.size());
        parent_.tun_recv(output);
        if (!firstOutboundPacket_) {
            firstOutboundPacket_ = true;
            Stage("LWIP_FIRST_PACKET_HANDED_TO_OPENVPN");
        }
        std::lock_guard lock(g.mutex);
        g.outboundBridge = true;
        if (destination && ip4_addr1(destination) == 1 && ip4_addr2(destination) == 1 &&
            ip4_addr3(destination) == 1 && ip4_addr4(destination) == 1) g.dnsPacketToTunnel = true;
        return ERR_OK;
    }
    void InitLwip() {
        lwip_init();
        ip4_addr_t ip{}, mask{}, gateway{};
        if (!ip4addr_aton(address_.c_str(), &ip)) throw std::runtime_error("Bad Nord IPv4");
        if (gateway_.empty() || !ip4addr_aton(gateway_.c_str(), &gateway))
            throw std::runtime_error("Nord did not provide an IPv4 tunnel gateway");
        const u32_t maskValue = prefix_ ? 0xffffffffu << (32 - prefix_) : 0;
        mask.addr = lwip_htonl(maskValue);
        if (!netif_add(&netif_, &ip, &mask, &gateway, this, InterfaceInit, ip4_input))
            throw std::runtime_error("lwIP netif setup failed");
        netif_set_default(&netif_); netif_set_up(&netif_); netif_set_link_up(&netif_);
        ip4_addr_t dns{}; IP4_ADDR(&dns, 1, 1, 1, 1); dns_setserver(0, &dns);
        ready_ = tunnelUp_ = true;
        Stage("LWIP_IPV4_NETIF_READY");
    }
    void StartDns() {
        ip_addr_t address{};
        const err_t result = dns_gethostbyname(kHost, &address, DnsDone, this);
        if (result == ERR_OK) DnsDone(kHost, &address, this);
        else if (result != ERR_INPROGRESS) Finish(false, "lwIP DNS could not start");
    }
    void StartProxy() {
        listener_ = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
        if (listener_ == INVALID_SOCKET) throw std::runtime_error("Loopback proxy socket failed");
        sockaddr_in endpoint{};
        endpoint.sin_family = AF_INET;
        endpoint.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
        endpoint.sin_port = 0;
        if (bind(listener_, reinterpret_cast<sockaddr*>(&endpoint), sizeof(endpoint)) == SOCKET_ERROR ||
            listen(listener_, SOMAXCONN) == SOCKET_ERROR)
            throw std::runtime_error("Loopback proxy bind failed");
        int size = sizeof(endpoint);
        getsockname(listener_, reinterpret_cast<sockaddr*>(&endpoint), &size);
        proxyPort_ = ntohs(endpoint.sin_port);
        Stage("LOOPBACK_PROXY_LISTENING_AWAITING_TUNNEL_PROBE");
    }
    void StartReadinessProbe() {
        readinessProbeIndex_ = 0;
        readinessCountryMatches_ = 0;
        StartCurrentCountryProbe();
    }
    void StartCurrentCountryProbe() {
        if (readinessProbeIndex_ >= kCountryProbes.size()) {
            if (readinessCountryMatches_ < 2) {
                Finish(false, "Independent country verification did not reach a majority");
                return;
            }
            {
                std::lock_guard lock(g.mutex);
                g.canada = g_expectedCountry == "CA";
            }
            PublishProxyReady();
            return;
        }
        readinessResponse_.clear();
        Stage("COUNTRY_CONSENSUS_DNS_START_" + std::to_string(readinessProbeIndex_ + 1));
        ip_addr_t address{};
        const auto& probe = kCountryProbes[readinessProbeIndex_];
        const err_t result = dns_gethostbyname(probe.host, &address, ReadinessDnsDone, this);
        if (result == ERR_INPROGRESS) Stage("COUNTRY_CONSENSUS_DNS_QUEUED_" + std::to_string(readinessProbeIndex_ + 1));
        if (result == ERR_OK) ReadinessDnsDone(probe.host, &address, this);
        else if (result != ERR_INPROGRESS) Finish(false, "Tunnel readiness DNS could not start");
    }
    void PublishProxyReady() {
        if (stopped_ || !tunnelUp_ || proxyPort_ == 0) { Finish(false, "Tunnel ended before proxy readiness"); return; }
        {
            std::lock_guard lock(g.mutex);
            g.proxyReady = true;
        }
        Stage("TUNNEL_DNS_TCP_COUNTRY_CONSENSUS_VERIFIED");
        std::cout << "PROXY_READY " << proxyPort_ << std::endl;
        acceptThread_ = std::thread([this] { AcceptLoop(); });
    }
    void AcceptLoop() {
        while (!stopped_) {
            SOCKET browser = accept(listener_, nullptr, nullptr);
            if (browser == INVALID_SOCKET) break;
            std::thread([this, browser] {
                std::string header;
                char byte{};
                while (header.size() < 32768 && recv(browser, &byte, 1, 0) == 1) {
                    header.push_back(byte);
                    if (header.ends_with("\r\n\r\n")) break;
                }
                const auto lineEnd = header.find("\r\n");
                const auto first = header.substr(0, lineEnd);
                if (!first.starts_with("CONNECT ")) { closesocket(browser); return; }
                const auto targetEnd = first.find(' ', 8);
                const auto target = first.substr(8, targetEnd == std::string::npos ? std::string::npos : targetEnd - 8);
                const auto colon = target.rfind(':');
                if (colon == std::string::npos) { closesocket(browser); return; }
                const auto host = target.substr(0, colon);
                const int port = std::atoi(target.substr(colon + 1).c_str());
                if (host.empty() || port < 1 || port > 65535) { closesocket(browser); return; }
                std::lock_guard lock(pendingMutex_);
                pending_.push_back({browser, host, static_cast<uint16_t>(port)});
                if (g_proxyDiagnostics) std::cerr << "PROXY_STAGE accepted_connect" << std::endl;
            }).detach();
        }
    }
    void ProcessProxyWork() {
        std::deque<PendingConnect> additions;
        { std::lock_guard lock(pendingMutex_); additions.swap(pending_); }
        for (auto& pending : additions) {
            auto flow = std::make_unique<Flow>();
            flow->owner = this; flow->browser = pending.browser; flow->host = std::move(pending.host); flow->port = pending.port;
            Flow* raw = flow.get(); flows_.push_back(std::move(flow));
            ip_addr_t resolved{};
            const err_t result = dns_gethostbyname(raw->host.c_str(), &resolved, ProxyDnsDone, raw);
            if (result == ERR_OK) ProxyDnsDone(raw->host.c_str(), &resolved, raw);
            else if (result != ERR_INPROGRESS) CloseFlow(raw);
        }
        for (auto& flow : flows_) {
            if (flow->browserClosed && !flow->closing) CloseFlow(flow.get());
            if (!flow->connected || flow->closing || !flow->pcb) continue;
            std::deque<std::vector<unsigned char>> chunks;
            { std::lock_guard lock(flow->inputMutex); chunks.swap(flow->input); }
            for (auto& chunk : chunks) {
                size_t offset{};
                while (offset < chunk.size()) {
                    const auto available = tcp_sndbuf(flow->pcb);
                    if (!available) { std::lock_guard lock(flow->inputMutex); flow->input.push_front(std::vector<unsigned char>(chunk.begin() + offset, chunk.end())); break; }
                    const auto count = static_cast<u16_t>(std::min<size_t>({chunk.size() - offset, available, 16384}));
                    if (tcp_write(flow->pcb, chunk.data() + offset, count, TCP_WRITE_FLAG_COPY) != ERR_OK) { CloseFlow(flow.get()); break; }
                    offset += count;
                }
                if (flow->closing) break;
            }
            if (flow->pcb && !flow->closing) tcp_output(flow->pcb);
        }
        for (auto& flow : flows_)
            if (flow->closing && flow->reader.joinable()) flow->reader.join();
        std::erase_if(flows_, [](const auto& flow) { return flow->closing && !flow->reader.joinable(); });
    }
    static void ProxyDnsDone(const char*, const ip_addr_t* address, void* context) {
        auto* flow = static_cast<Flow*>(context);
        if (!address || !IP_IS_V4(address) || flow->closing) { flow->owner->CloseFlow(flow); return; }
        if (g_proxyDiagnostics) std::cerr << "PROXY_STAGE dns_resolved" << std::endl;
        flow->pcb = tcp_new_ip_type(IPADDR_TYPE_V4);
        if (!flow->pcb) { flow->owner->CloseFlow(flow); return; }
        tcp_arg(flow->pcb, flow); tcp_recv(flow->pcb, ProxyReceived); tcp_err(flow->pcb, ProxyError);
        if (tcp_connect(flow->pcb, ip_2_ip4(address), flow->port, ProxyConnected) != ERR_OK) flow->owner->CloseFlow(flow);
    }
    static err_t ProxyConnected(void* context, tcp_pcb*, err_t error) {
        auto* flow = static_cast<Flow*>(context);
        if (error != ERR_OK || flow->closing) { flow->owner->CloseFlow(flow); return error; }
        static constexpr char response[] = "HTTP/1.1 200 Connection Established\r\n\r\n";
        if (send(flow->browser, response, sizeof(response) - 1, 0) == SOCKET_ERROR) { flow->owner->CloseFlow(flow); return ERR_ABRT; }
        flow->connected = true;
        if (g_proxyDiagnostics) std::cerr << "PROXY_STAGE tcp_connected" << std::endl;
        flow->reader = std::thread([flow] {
            std::vector<unsigned char> buffer(16384);
            while (!flow->closing) {
                const int count = recv(flow->browser, reinterpret_cast<char*>(buffer.data()), static_cast<int>(buffer.size()), 0);
                if (count <= 0) break;
                std::lock_guard lock(flow->inputMutex);
                flow->input.emplace_back(buffer.begin(), buffer.begin() + count);
            }
            flow->browserClosed = true;
        });
        return ERR_OK;
    }
    static err_t ProxyReceived(void* context, tcp_pcb* pcb, pbuf* data, err_t error) {
        auto* flow = static_cast<Flow*>(context);
        if (error != ERR_OK || !data) { if (data) pbuf_free(data); flow->owner->CloseFlow(flow); return error; }
        std::vector<unsigned char> bytes(data->tot_len);
        pbuf_copy_partial(data, bytes.data(), data->tot_len, 0);
        tcp_recved(pcb, data->tot_len); pbuf_free(data);
        size_t offset{};
        while (offset < bytes.size()) {
            const int sent = send(flow->browser, reinterpret_cast<const char*>(bytes.data() + offset), static_cast<int>(bytes.size() - offset), 0);
            if (sent <= 0) { flow->owner->CloseFlow(flow); return ERR_ABRT; }
            offset += sent;
        }
        return ERR_OK;
    }
    static void ProxyError(void* context, err_t) {
        auto* flow = static_cast<Flow*>(context); flow->pcb = nullptr; flow->owner->CloseFlow(flow);
    }
    void CloseFlow(Flow* flow) {
        const bool wasClosing = flow->closing.exchange(true);
        if (!wasClosing && flow->pcb) { tcp_arg(flow->pcb, nullptr); tcp_recv(flow->pcb, nullptr); tcp_err(flow->pcb, nullptr); tcp_abort(flow->pcb); flow->pcb = nullptr; }
        if (flow->browser != INVALID_SOCKET) { shutdown(flow->browser, SD_BOTH); closesocket(flow->browser); flow->browser = INVALID_SOCKET; }
    }
    static void DnsDone(const char*, const ip_addr_t* address, void* context) {
        auto* self = static_cast<PacketClient*>(context);
        if (!address || !IP_IS_V4(address)) { Finish(false, "Tunnel DNS failed"); return; }
        {
            std::lock_guard lock(g.mutex); g.dnsResolved = true;
        }
        self->pcb_ = tcp_new_ip_type(IPADDR_TYPE_V4);
        if (!self->pcb_) { Finish(false, "lwIP TCP allocation failed"); return; }
        tcp_arg(self->pcb_, self); tcp_recv(self->pcb_, Received); tcp_err(self->pcb_, TcpError);
        if (tcp_connect(self->pcb_, ip_2_ip4(address), 80, Connected) != ERR_OK)
            Finish(false, "lwIP TCP connect could not start");
    }
    static void ReadinessDnsDone(const char*, const ip_addr_t* address, void* context) {
        auto* self = static_cast<PacketClient*>(context);
        if (!address || !IP_IS_V4(address)) { Finish(false, "Tunnel readiness DNS failed"); return; }
        {
            std::lock_guard lock(g.mutex);
            g.dnsResolved = true;
        }
        Stage("COUNTRY_CONSENSUS_DNS_RESOLVED_" + std::to_string(self->readinessProbeIndex_ + 1));
        self->readinessPcb_ = tcp_new_ip_type(IPADDR_TYPE_V4);
        if (!self->readinessPcb_) { Finish(false, "Tunnel readiness TCP allocation failed"); return; }
        tcp_arg(self->readinessPcb_, self); tcp_recv(self->readinessPcb_, ReadinessReceived); tcp_err(self->readinessPcb_, ReadinessError);
        if (tcp_connect(self->readinessPcb_, ip_2_ip4(address), 80, ReadinessConnected) != ERR_OK)
            Finish(false, "Tunnel readiness TCP connect could not start");
    }
    static err_t ReadinessConnected(void* context, tcp_pcb* pcb, err_t error) {
        auto* self = static_cast<PacketClient*>(context);
        if (error != ERR_OK) { Finish(false, "Tunnel readiness TCP connect failed"); return error; }
        {
            std::lock_guard lock(g.mutex);
            g.tcpConnected = true;
            g.windowsDestinationTcpSockets = CountProcessRemoteTcpSockets();
        }
        Stage("COUNTRY_CONSENSUS_TCP_CONNECTED_" + std::to_string(self->readinessProbeIndex_ + 1));
        const auto& probe = kCountryProbes[self->readinessProbeIndex_];
        const std::string request = "GET " + std::string(probe.path) + " HTTP/1.1\r\nHost: " +
            probe.host + "\r\nUser-Agent: MonkeyTV-Disposable-Proof\r\nConnection: close\r\n\r\n";
        const err_t written = tcp_write(pcb, request.data(), static_cast<u16_t>(request.size()), TCP_WRITE_FLAG_COPY);
        if (written != ERR_OK) { Finish(false, "Tunnel readiness TCP write failed"); return written; }
        tcp_output(pcb);
        return ERR_OK;
    }
    static err_t ReadinessReceived(void* context, tcp_pcb*, pbuf* data, err_t error) {
        auto* self = static_cast<PacketClient*>(context);
        if (error != ERR_OK || !data) { if (data) pbuf_free(data); Finish(false, "Tunnel readiness TCP receive failed"); return error; }
        std::vector<char> bytes(data->tot_len);
        pbuf_copy_partial(data, bytes.data(), data->tot_len, 0); tcp_recved(self->readinessPcb_, data->tot_len); pbuf_free(data);
        self->readinessResponse_.append(bytes.data(), bytes.size());
        const auto headerEnd = self->readinessResponse_.find("\r\n\r\n");
        if (headerEnd == std::string::npos) return ERR_OK;
        if (self->readinessResponse_.find(" 200 ") == std::string::npos) {
            Finish(false, "Country verification provider returned a non-success response");
            return ERR_ABRT;
        }
        const auto body = self->readinessResponse_.substr(headerEnd + 4);
        std::string observed;
        if (self->readinessProbeIndex_ == 1) {
            // ipwho.is returns JSON. Select the explicit field instead of the
            // first two uppercase characters (which may be a continent code).
            std::smatch match;
            if (std::regex_search(body, match,
                    std::regex("\\\"country_code\\\"\\s*:\\s*\\\"([A-Z]{2})\\\"")))
                observed = match[1].str();
        } else {
            for (size_t i = 0; i + 1 < body.size(); ++i) {
                if (body[i] >= 'A' && body[i] <= 'Z' && body[i + 1] >= 'A' && body[i + 1] <= 'Z') {
                    observed = body.substr(i, 2);
                    break;
                }
            }
        }
        if (observed.empty()) return ERR_OK;
        if (observed != g_expectedCountry) {
            Stage("COUNTRY_CONSENSUS_OUTLIER_" + std::to_string(self->readinessProbeIndex_ + 1));
        } else {
            ++self->readinessCountryMatches_;
            Stage("COUNTRY_CONSENSUS_MATCH_" + std::to_string(self->readinessProbeIndex_ + 1));
        }
        if (self->readinessPcb_) {
            // The readiness flow has succeeded. Detach callbacks before aborting the
            // one-shot PCB so its deliberate teardown cannot be mistaken for a
            // tunnel failure by ReadinessError.
            tcp_arg(self->readinessPcb_, nullptr);
            tcp_recv(self->readinessPcb_, nullptr);
            tcp_err(self->readinessPcb_, nullptr);
            tcp_abort(self->readinessPcb_);
            self->readinessPcb_ = nullptr;
        }
        ++self->readinessProbeIndex_;
        self->StartCurrentCountryProbe();
        return ERR_ABRT;
    }
    static void ReadinessError(void*, err_t) { Finish(false, "Tunnel readiness TCP error"); }
    static err_t Connected(void* context, tcp_pcb* pcb, err_t error) {
        auto* self = static_cast<PacketClient*>(context);
        if (error != ERR_OK) { Finish(false, "lwIP TCP connect failed"); return error; }
        {
            std::lock_guard lock(g.mutex);
            g.tcpConnected = true;
            g.windowsDestinationTcpSockets = CountProcessRemoteTcpSockets();
        }
        const err_t written = tcp_write(pcb, kRequest, sizeof(kRequest) - 1, TCP_WRITE_FLAG_COPY);
        if (written != ERR_OK) { Finish(false, "lwIP HTTP write failed"); return written; }
        tcp_output(pcb);
        self->tcpWasActive_ = true;
        return ERR_OK;
    }
    static err_t Received(void* context, tcp_pcb* pcb, pbuf* data, err_t error) {
        auto* self = static_cast<PacketClient*>(context);
        if (error != ERR_OK) { if (data) pbuf_free(data); Finish(false, "lwIP TCP receive failed"); return error; }
        if (!data) { Finish(false, "TCP closed before Canada response"); return ERR_OK; }
        std::vector<char> bytes(data->tot_len);
        pbuf_copy_partial(data, bytes.data(), data->tot_len, 0);
        tcp_recved(pcb, data->tot_len); pbuf_free(data);
        self->response_.append(bytes.data(), bytes.size());
        if (self->response_.find("\r\nCA\r\n") != std::string::npos ||
            self->response_.ends_with("\nCA\n")) {
            {
                std::lock_guard lock(g.mutex); g.canada = true;
            }
            // Interrupt the outer tunnel while this lwIP PCB is still active.
            self->tunnelUp_ = false;
            pbuf* probe = pbuf_alloc(PBUF_RAW, 1, PBUF_RAM);
            const err_t rejected = probe ? self->SendPacket(probe, nullptr) : ERR_MEM;
            if (probe) pbuf_free(probe);
            {
                std::lock_guard lock(g.mutex); g.failClosed = rejected == ERR_RTE;
            }
            Finish(rejected == ERR_RTE, rejected == ERR_RTE ? "PASS" : "Fail-closed output escaped");
        }
        return ERR_OK;
    }
    static void TcpError(void*, err_t) { Finish(false, "lwIP TCP error"); }
    void Tick() {
        if (stopped_) return;
        timer_.expires_after(openvpn::Time::Duration::milliseconds(50));
        timer_.async_wait([this](const openvpn_io::error_code& error) {
            if (!error && !stopped_) { sys_check_timeouts(); if (g_proxyMode) ProcessProxyWork(); Tick(); }
        });
    }
    void Cleanup() {
        if (stopped_.exchange(true)) return;
        tunnelUp_ = false; timer_.cancel();
        if (listener_ != INVALID_SOCKET) { closesocket(listener_); listener_ = INVALID_SOCKET; }
        if (acceptThread_.joinable()) acceptThread_.join();
        for (auto& flow : flows_) {
            CloseFlow(flow.get());
            if (flow->reader.joinable()) flow->reader.join();
        }
        flows_.clear();
        if (pcb_) { tcp_arg(pcb_, nullptr); tcp_recv(pcb_, nullptr); tcp_err(pcb_, nullptr); tcp_abort(pcb_); pcb_ = nullptr; }
        if (readinessPcb_) { tcp_arg(readinessPcb_, nullptr); tcp_recv(readinessPcb_, nullptr); tcp_err(readinessPcb_, nullptr); tcp_abort(readinessPcb_); readinessPcb_ = nullptr; }
        if (ready_) netif_remove(&netif_);
        ready_ = false;
    }

    openvpn::AsioTimer timer_;
    PacketFactory* factory_;
    openvpn::TunClientParent& parent_;
    openvpn::TunProp::State::Ptr state_;
    netif netif_{};
    tcp_pcb* pcb_{};
    tcp_pcb* readinessPcb_{};
    std::string address_, gateway_, response_, readinessResponse_;
    size_t readinessProbeIndex_{};
    size_t readinessCountryMatches_{};
    int prefix_{}, mtu_{1400};
    bool ready_{}, tunnelUp_{}, tcpWasActive_{}, firstOutboundPacket_{};
    std::atomic<bool> stopped_{};
    SOCKET listener_{INVALID_SOCKET};
    uint16_t proxyPort_{};
    std::thread acceptThread_;
    std::mutex pendingMutex_;
    std::deque<PendingConnect> pending_;
    std::vector<std::unique_ptr<Flow>> flows_;
};

openvpn::TunClient::Ptr PacketFactory::new_tun_client_obj(openvpn_io::io_context& io,
    openvpn::TunClientParent& parent, openvpn::TransportClient*) {
    return openvpn::TunClient::Ptr(new PacketClient(io, this, parent));
}

class ProofClient final : public openvpn::ClientAPI::OpenVPNClient {
public:
#ifndef MONKEYTV_REFERENCE_TRANSPORT
    openvpn::TunClientFactory* new_tun_factory(const openvpn::ExternalTun::Config& config,
                                               const openvpn::OptionList&) override {
        Stage("EXTERNAL_TUN_FACTORY_INVOKED");
        { std::lock_guard lock(g.mutex); g.tunFactory = true; }
        return new PacketFactory(config);
    }
#endif
    bool socket_protect(openvpn_io::detail::socket_type, std::string, bool ipv6) override {
        Stage(ipv6 ? "SOCKET_PROTECT_IPV6" : "SOCKET_PROTECT_IPV4");
        { std::lock_guard lock(g.mutex); g.socketProtect = true; }
        return true;
    }
    void event(const openvpn::ClientAPI::Event& event) override {
        { std::lock_guard lock(g.mutex); g.firstEvent = true; }
        Stage("OPENVPN_EVENT_" + event.name, event.error, event.info);
        if (event.error) Finish(false, "OpenVPN event " + event.name + ": " + SanitizeFailure(event.info));
        else if (g_proxyMode && event.name == "DISCONNECTED") Finish(false, "OpenVPN tunnel disconnected after readiness");
    }
    void log(const openvpn::ClientAPI::LogInfo& log) override {
        bool first{};
        { std::lock_guard lock(g.mutex); first = !g.firstLog; g.firstLog = true; }
        if (first) Stage("FIRST_OPENVPN_LOG_CALLBACK");
        std::string lower = log.text;
        std::transform(lower.begin(), lower.end(), lower.begin(),
                       [](unsigned char value) { return static_cast<char>(std::tolower(value)); });
        if (lower.find("transport") != std::string::npos || lower.find("contacting") != std::string::npos)
            Stage("OPENVPN_LOG_TRANSPORT");
        if (lower.find("tls") != std::string::npos) Stage("OPENVPN_LOG_TLS");
        if (lower.find("auth") != std::string::npos) Stage("OPENVPN_LOG_AUTH");
        if (lower.find("error") != std::string::npos || lower.find("fatal") != std::string::npos)
            Stage("OPENVPN_LOG_FAILURE", true, log.text);
    }
    void acc_event(const openvpn::ClientAPI::AppCustomControlMessageEvent&) override {}
    void external_pki_cert_request(openvpn::ClientAPI::ExternalPKICertRequest& r) override { r.error = true; }
    void external_pki_sign_request(openvpn::ClientAPI::ExternalPKISignRequest& r) override { r.error = true; }
    bool pause_on_connection_timeout() override { return false; }
};

void Finish(bool pass, std::string reason) {
    bool first{};
    {
        std::lock_guard lock(g.mutex);
        if (!g.completed) { g.completed = true; if (!pass) g.error = std::move(reason); first = true; }
    }
    if (first && g_client) g_client->stop();
}

std::string Utf8(const wchar_t* text) {
    if (!text || !*text) return {};
    int count = WideCharToMultiByte(CP_UTF8, 0, text, -1, nullptr, 0, nullptr, nullptr);
    std::vector<char> bytes(count);
    WideCharToMultiByte(CP_UTF8, 0, text, -1, bytes.data(), count, nullptr, nullptr);
    return std::string(bytes.data());
}

bool Credentials(std::string& username, std::string& password) {
    CREDUI_INFOW info{sizeof(info)};
    info.pszCaptionText = L"Monkey TV disposable Nord proof";
    info.pszMessageText = L"Enter NordVPN service credentials. They will not be saved.";
    ULONG package{}; void* packed{}; ULONG packedSize{}; BOOL save = FALSE;
    if (CredUIPromptForWindowsCredentialsW(&info, 0, &package, nullptr, 0, &packed,
        &packedSize, &save, CREDUIWIN_GENERIC) != ERROR_SUCCESS) return false;
    wchar_t user[512]{}, domain[256]{}, secret[512]{};
    DWORD userSize=512, domainSize=256, secretSize=512;
    BOOL ok = CredUnPackAuthenticationBufferW(0, packed, packedSize, user, &userSize,
        domain, &domainSize, secret, &secretSize);
    SecureZeroMemory(packed, packedSize); CoTaskMemFree(packed);
    if (ok) { username = Utf8(user); password = Utf8(secret); }
    SecureZeroMemory(user, sizeof(user)); SecureZeroMemory(domain, sizeof(domain));
    SecureZeroMemory(secret, sizeof(secret));
    return ok && !username.empty() && !password.empty();
}

std::string Read(const char* path) {
    std::ifstream file(path, std::ios::binary);
    if (!file) throw std::runtime_error("Cannot read official Nord .ovpn file");
    return {std::istreambuf_iterator<char>(file), {}};
}

std::string ExpectedCountryFromConfig(const std::string& config) {
    std::smatch match;
    if (!std::regex_search(config, match, std::regex(R"(verify-x509-name\s+CN=(us|ca|uk)\d+\.nordvpn\.com)", std::regex_constants::icase)))
        throw std::runtime_error("Official Nord profile does not identify a supported country");
    std::string prefix = match[1].str();
    std::transform(prefix.begin(), prefix.end(), prefix.begin(), [](unsigned char value) { return static_cast<char>(std::tolower(value)); });
    return prefix == "ca" ? "CA" : prefix == "us" ? "US" : "GB";
}
}

int main(int argc, char** argv) {
    const bool proxyPromptMode = argc == 3 && std::string(argv[1]) == "--proxy-prompt";
    const bool proxyDiagnosticsMode = argc == 3 && std::string(argv[1]) == "--proxy-diagnostics";
    proof::g_proxyDiagnostics = proxyPromptMode || proxyDiagnosticsMode;
    proof::g_proxyMode = argc == 3 && (std::string(argv[1]) == "--proxy" || proxyPromptMode || proxyDiagnosticsMode);
    const char* configPath = proof::g_proxyMode ? (argc == 3 ? argv[2] : nullptr) : (argc == 2 ? argv[1] : nullptr);
    if (!configPath) { std::cerr << "Expected [--proxy] official Nord .ovpn path\n"; return 2; }
    try {
        std::string user, password;
        if (proxyPromptMode) {
            if (!proof::Credentials(user, password)) return 3;
        } else if (proof::g_proxyMode) {
            if (!std::getline(std::cin, user) || !std::getline(std::cin, password) || user.empty() || password.empty()) return 3;
        } else if (!proof::Credentials(user, password)) return 3;
        proof::ProofClient client; proof::g_client = &client;
        openvpn::ClientAPI::Config config; config.content = proof::Read(configPath);
        proof::g_expectedCountry = proof::ExpectedCountryFromConfig(config.content);
        config.contentList.emplace_back("ignore-unknown-option", "ping-timer-rem");
        config.allowUnusedAddrFamilies = "no"; config.tunPersist = false;
        config.googleDnsFallback = false;
        config.disableClientCert = true;
        // Nord's official profiles declare "comp-lzo no". Their servers still push
        // legacy LZO framing, which OpenVPN 3 correctly rejects in its default "no"
        // mode. "asym" is the documented OpenVPN 3 mode that accepts server-to-client
        // framing while never compressing client uplink traffic.
        config.compressionMode = "asym";
        auto eval = client.eval_config(config);
        if (eval.error) throw std::runtime_error(eval.message);
        proof::Stage("CONFIG_EVAL_OK");
        proof::Stage("SELECTED_REMOTE host=" + eval.remoteHost + " port=" + eval.remotePort +
                     " proto=" + eval.remoteProto);
        openvpn::ClientAPI::ProvideCreds creds; creds.username = user; creds.password = password;
        auto credResult = client.provide_creds(creds);
        std::fill(password.begin(), password.end(), '\0');
        std::fill(creds.password.begin(), creds.password.end(), '\0');
        if (credResult.error) throw std::runtime_error("Credential setup failed");
        proof::Stage("CREDENTIAL_PROVISION_OK");
        openvpn::ClientAPI::Status status;
        std::atomic<bool> workerDone{};
        std::thread worker([&] {
            { std::lock_guard lock(proof::g.mutex); proof::g.connectWorkerStarted = true; }
            proof::Stage("CONNECT_WORKER_START");
            status = client.connect();
            proof::Stage("CONNECT_WORKER_RETURN", status.error, status.message);
            workerDone = true;
        });
        for (int i = 0; i < 600 && !workerDone; ++i) {
            std::this_thread::sleep_for(std::chrono::milliseconds(100));
            std::lock_guard lock(proof::g.mutex);
            if (proof::g.completed || (proof::g_proxyMode && proof::g.proxyReady)) break;
        }
        if (proof::g_proxyMode && proof::g.proxyReady && !workerDone) {
            std::string command;
            while (std::getline(std::cin, command) && command != "STOP") { }
            client.stop();
        }
        if (!workerDone) {
            bool completed{};
            { std::lock_guard lock(proof::g.mutex); completed = proof::g.completed; }
            if (!completed) proof::Finish(false, "Timeout before transport event");
            client.stop();
        }
        worker.join(); proof::g_client = nullptr;
        std::fill(user.begin(), user.end(), '\0');
        std::lock_guard lock(proof::g.mutex);
        std::cout << "OpenVPN=" << proof::g.openVpnConnected << "\nBridgeOut=" << proof::g.outboundBridge
                  << "\nExternalTunnelReady=" << proof::g.externalTunnelReady
                  << "\nFirstLogCallback=" << proof::g.firstLog
                  << "\nFirstEventCallback=" << proof::g.firstEvent
                  << "\nSocketProtect=" << proof::g.socketProtect
                  << "\nTunFactory=" << proof::g.tunFactory
                  << "\nConnectWorkerStarted=" << proof::g.connectWorkerStarted
                  << "\nBridgeIn=" << proof::g.inboundBridge << "\nDnsPacket=" << proof::g.dnsPacketToTunnel
                  << "\nDnsResolved=" << proof::g.dnsResolved << "\nTcpConnected=" << proof::g.tcpConnected
                  << "\nWindowsDestinationTcpSockets=" << proof::g.windowsDestinationTcpSockets
                  << "\nCanada=" << proof::g.canada << "\nFailClosed=" << proof::g.failClosed << '\n';
        if (!proof::g.error.empty()) std::cout << "Error=" << proof::g.error << '\n';
        if (status.error && proof::g.error.empty()) std::cout << "OpenVPNError=" << status.message << '\n';
        std::cout << "StageTimelineBegin\n";
        for (const auto& stage : proof::g.stages) std::cout << stage << '\n';
        std::cout << "StageTimelineEnd\n";
        if (proof::g_proxyMode) return proof::g.proxyReady && !status.error ? 0 : 1;
        return proof::g.canada && proof::g.failClosed && proof::g.windowsDestinationTcpSockets == 0 ? 0 : 1;
    } catch (const std::exception& e) { std::cerr << "Fatal=" << e.what() << '\n'; return 1; }
}
