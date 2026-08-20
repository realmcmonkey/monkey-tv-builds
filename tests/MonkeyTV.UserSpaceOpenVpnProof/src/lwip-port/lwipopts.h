#pragma once

#define NO_SYS                          1
#define SYS_LIGHTWEIGHT_PROT            0
#define LWIP_IPV4                       1
#define LWIP_IPV6                       0
#define LWIP_ARP                        0
#define LWIP_ETHERNET                   0
#define LWIP_RAW                        0
#define LWIP_UDP                        1
#define LWIP_TCP                        1
#define LWIP_DNS                        1
#define LWIP_DHCP                       0
#define LWIP_AUTOIP                     0
#define LWIP_IGMP                       0
#define LWIP_NETCONN                    0
#define LWIP_SOCKET                     0
#define LWIP_STATS                      0
#define LWIP_CHECKSUM_CTRL_PER_NETIF    0
#define MEM_ALIGNMENT                   8
#define MEM_SIZE                        (8 * 1024 * 1024)
#define MEMP_NUM_TCP_PCB                64
#define MEMP_NUM_TCP_SEG                1024
#define PBUF_POOL_SIZE                  512
#define PBUF_POOL_BUFSIZE               1600
#define TCP_MSS                         1360
#define TCP_SND_BUF                     (16 * TCP_MSS)
#define TCP_WND                         (16 * TCP_MSS)
#define DNS_TABLE_SIZE                  4
#define DNS_MAX_NAME_LENGTH             256
#define LWIP_RAND()                     ((unsigned int)rand())
#define LWIP_TIMEVAL_PRIVATE            0
