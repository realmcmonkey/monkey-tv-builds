cmake_minimum_required(VERSION 3.25)
project(MonkeyTVUserSpaceOpenVpnHelper LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

find_package(OpenSSL REQUIRED)
find_package(lz4 CONFIG REQUIRED)
find_package(xxHash CONFIG REQUIRED)

set(LWIP_DIR "${CMAKE_CURRENT_SOURCE_DIR}/vendor/lwip")
set(LWIP_EXCLUDE_SLIPIF TRUE)
include("${LWIP_DIR}/src/Filelists.cmake")

add_library(monkeytv_lwip STATIC ${lwipcore_SRCS} ${lwipcore4_SRCS})
target_include_directories(monkeytv_lwip PUBLIC
    src/lwip-port
    vendor/lwip/src/include)

add_executable(MonkeyTV.UserSpaceOpenVpnHelper
    src/main.cpp
    vendor/openvpn3/client/ovpncli.cpp
    vendor/openvpn3/openvpn/crypto/data_epoch.cpp)

add_executable(MonkeyTV.OpenVpnReferenceTransportCheck
    src/main.cpp
    vendor/openvpn3/client/ovpncli.cpp
    vendor/openvpn3/openvpn/crypto/data_epoch.cpp)

target_include_directories(MonkeyTV.UserSpaceOpenVpnHelper PRIVATE
    vendor/openvpn3
    vendor/lwip/src/include)

target_include_directories(MonkeyTV.OpenVpnReferenceTransportCheck PRIVATE
    vendor/openvpn3
    vendor/lwip/src/include)

target_compile_definitions(MonkeyTV.UserSpaceOpenVpnHelper PRIVATE
    ASIO_STANDALONE
    ASIO_NO_DEPRECATED
    HAVE_LZ4
    NOMINMAX
    OPENVPN_EXTERNAL_TUN_FACTORY
    OPENVPN_FORCE_TUN_NULL
    USE_ASIO
    USE_OPENSSL
    WIN32_LEAN_AND_MEAN
    _CRT_SECURE_NO_WARNINGS
    _WIN32_WINNT=0x0A00)

target_compile_definitions(MonkeyTV.OpenVpnReferenceTransportCheck PRIVATE
    ASIO_STANDALONE
    ASIO_NO_DEPRECATED
    HAVE_LZ4
    MONKEYTV_REFERENCE_TRANSPORT
    NOMINMAX
    OPENVPN_FORCE_TUN_NULL
    USE_ASIO
    USE_OPENSSL
    WIN32_LEAN_AND_MEAN
    _CRT_SECURE_NO_WARNINGS
    _WIN32_WINNT=0x0A00)

target_compile_options(MonkeyTV.UserSpaceOpenVpnHelper PRIVATE /W4 /EHsc)
target_compile_options(MonkeyTV.OpenVpnReferenceTransportCheck PRIVATE /W4 /EHsc)
target_link_libraries(MonkeyTV.UserSpaceOpenVpnHelper PRIVATE
    monkeytv_lwip
    OpenSSL::SSL
    OpenSSL::Crypto
    lz4::lz4
    xxHash::xxhash
    advapi32
    crypt32
    credui
    fwpuclnt
    iphlpapi
    ole32
    rpcrt4
    shell32
    ws2_32)

target_link_libraries(MonkeyTV.OpenVpnReferenceTransportCheck PRIVATE
    monkeytv_lwip
    OpenSSL::SSL
    OpenSSL::Crypto
    lz4::lz4
    xxHash::xxhash
    advapi32
    crypt32
    credui
    fwpuclnt
    iphlpapi
    ole32
    rpcrt4
    shell32
    ws2_32)

# Production currently resolves this exact Runtime/VpnV2 helper filename. Keep the
# output contract stable while the maintained CMake target uses its production name.
set_target_properties(MonkeyTV.UserSpaceOpenVpnHelper PROPERTIES OUTPUT_NAME "MonkeyTV.UserSpaceOpenVpnProof")
