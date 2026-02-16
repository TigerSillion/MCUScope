#ifndef ICS2_RX26T_H
#define ICS2_RX26T_H

#include <stdint.h>


#define   ICS_SCI1_PD3_PD5    (0x10U)  //
#define   ICS_SCI5_PD7_PE0    (0x50U)  //
#define   ICS_SCI5_PB5_PB6    (0x51U)  //
#define   ICS_SCI6_P81_P80    (0x60U)  //
#define   ICS_SCI6_PB2_PB1    (0x61U)  //
#define   ICS_SCI12_PD4_PD6   (0xC0U)  //
#define   ICS_SCI12_P01_P00   (0xC1U)  //
#define   ICS_SCI12_P81_P80   (0xC2U)  //
#define   ICS_SCI12_P23_P22   (0xC3U)  //
#define   ICS_SCI12_PB5_PB6   (0xC4U)  //


void     ics2_init(void * addr, uint8_t port, uint8_t level, uint8_t speed, uint8_t mode);
void     ics2_watchpoint(void);
uint32_t ics2_version(void);

void ics_int_sci_eri(void);
void ics_int_sci_rxi(void);
void ics_int_sci_txi(void);

#endif /* ICS2_RX26T_H */
