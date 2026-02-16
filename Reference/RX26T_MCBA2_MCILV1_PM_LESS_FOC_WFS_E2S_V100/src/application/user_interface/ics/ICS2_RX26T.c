/***********************************************************************************************************************
* File Name   : ICS2_RX26T.c
* Description : UART protocol replacement for the original closed ICS2 library.
***********************************************************************************************************************/

#include "ICS2_RX26T.h"

#include <stdint.h>
#include <string.h>

#include "r_smc_entry.h"
#include "r_mtr_ics.h"
#include "r_motor_common.h"
#include "r_motor_current_api.h"
#include "r_motor_sensorless_vector_api.h"
#include "r_motor_speed_api.h"

#define UART_SYNC_0                     (0xAAU)
#define UART_SYNC_1                     (0x55U)

#define UART_CMD_PING                   (0x01U)
#define UART_CMD_GET_INFO               (0x02U)
#define UART_CMD_READ_VARIABLE          (0x10U)
#define UART_CMD_WRITE_VARIABLE         (0x11U)
#define UART_CMD_START_SCOPE            (0x20U)
#define UART_CMD_STOP_SCOPE             (0x21U)
#define UART_CMD_SCOPE_DATA             (0x22U)
#define UART_CMD_SET_CHANNELS           (0x23U)
#define UART_CMD_SET_TRIGGER            (0x24U)
#define UART_CMD_SET_SAMPLING           (0x25U)

#define UART_CMD_ACK                    (0x80U)
#define UART_CMD_NACK                   (0x81U)
#define UART_CMD_INFO_RESPONSE          (0x82U)
#define UART_CMD_VARIABLE_DATA          (0x83U)
#define UART_CMD_WAVEFORM_DATA          (0x84U)

#define UART_VAR_UINT8                  (0U)
#define UART_VAR_INT8                   (1U)
#define UART_VAR_UINT16                 (2U)
#define UART_VAR_INT16                  (3U)
#define UART_VAR_UINT32                 (4U)
#define UART_VAR_INT32                  (5U)
#define UART_VAR_FLOAT32                (6U)
#define UART_VAR_BOOL                   (7U)
#define UART_VAR_LOGIC                  (8U)

#define UART_RX_BUFFER_SIZE             (1024U)
#define UART_TX_BUFFER_SIZE             (16384U)
#define UART_MAX_RX_PAYLOAD             (256U)
#define UART_MAX_TX_PAYLOAD             (2048U)

#define UART_MAX_SCOPE_CHANNELS         (12U)
#define UART_MAX_SCOPE_RECORD_LENGTH    (256U)
#define UART_SCOPE_DYNAMIC_ENTRY_SIZE   (6U)  /* slot(1)+type(1)+addr(4) */

/*
 * Allow raw global-variable access without a fixed whitelist.
 * RX26T project map currently places RAM globals in 0x00000000-0x0000FFFF.
 * Keep the range configurable to avoid accidental access to invalid regions.
 */
#define UART_RAM_REGION0_START          (0x00000000UL)
#define UART_RAM_REGION0_END            (0x00010000UL) /* exclusive */
#define UART_RAM_REGION1_START          (0x00120040UL)
#define UART_RAM_REGION1_END            (0x001200B0UL) /* exclusive */

#define UART_SCI6_PFS_TX                (0x28U)
#define UART_SCI6_PFS_RX                (0x2AU)

typedef struct
{
    uint8_t      slot;
    uint8_t      type;
    uint32_t     addr;
} st_uart_scope_channel_t;

typedef struct
{
    uint32_t     addr;
    uint8_t      type;
} st_uart_var_ref_t;

typedef struct
{
    uint8_t  running;
    uint8_t  frame_ready;
    uint8_t  send_channel_cursor;
    uint8_t  channel_count;
    st_uart_scope_channel_t channels[UART_MAX_SCOPE_CHANNELS];
    uint16_t record_length;
    uint16_t sample_count;
    float    sample_period;
    float    trigger_position;
    float    trigger_level;
    uint8_t  trigger_source;
    uint8_t  trigger_mode;
    uint8_t  trigger_edge;
    float    buffer[UART_MAX_SCOPE_CHANNELS][UART_MAX_SCOPE_RECORD_LENGTH];
} st_uart_scope_state_t;

static volatile uint16_t s_rx_head = 0U;
static volatile uint16_t s_rx_tail = 0U;
static uint8_t s_rx_buffer[UART_RX_BUFFER_SIZE];

static volatile uint16_t s_tx_head = 0U;
static volatile uint16_t s_tx_tail = 0U;
static uint8_t s_tx_buffer[UART_TX_BUFFER_SIZE];

static st_uart_scope_state_t s_scope;
static uint8_t s_payload_buffer[7U + (UART_MAX_SCOPE_RECORD_LENGTH * 4U)];

static void serial_configure_pins(uint8_t port);
static void serial_init_sci6(uint8_t level, uint8_t speed);
static void serial_enqueue_start(void);
static uint16_t tx_count(void);
static uint16_t tx_free(void);
static uint8_t tx_enqueue(const uint8_t * data, uint16_t len);
static uint16_t rx_count(void);
static uint8_t rx_peek(uint16_t offset);
static void rx_drop(uint16_t count);

static uint8_t var_type_size(uint8_t type);
static uint8_t var_region_contains(uint32_t addr, uint8_t size, uint32_t start, uint32_t end);
static uint8_t var_address_is_valid(uint32_t addr, uint8_t size);
static uint8_t var_read_value(st_uart_var_ref_t var_ref, uint8_t * out_data);
static uint8_t var_write_value(st_uart_var_ref_t var_ref, const uint8_t * in_data, uint16_t len);
static float var_read_as_float(st_uart_var_ref_t var_ref, uint8_t * ok);

static uint8_t protocol_send_packet(uint8_t command, const uint8_t * payload, uint16_t len);
static void protocol_send_ack(void);
static void protocol_send_nack(void);
static void protocol_process_rx(void);
static void protocol_handle_command(uint8_t command, const uint8_t * payload, uint16_t len);

static void protocol_handle_get_info(void);
static void protocol_handle_read_variable(const uint8_t * payload, uint16_t len);
static void protocol_handle_write_variable(const uint8_t * payload, uint16_t len);
static void protocol_handle_start_scope(const uint8_t * payload, uint16_t len);
static void protocol_handle_stop_scope(void);
static void protocol_handle_set_trigger(const uint8_t * payload, uint16_t len);
static void protocol_handle_set_channels(const uint8_t * payload, uint16_t len);
static void protocol_handle_set_sampling(const uint8_t * payload, uint16_t len);

static void scope_reset(void);
static float scope_read_channel_value(const st_uart_scope_channel_t * p_channel, uint8_t * ok);
static void scope_sample_once(void);
static void scope_try_send_frame(void);
static uint8_t scope_build_waveform_payload(uint8_t channel_order, uint8_t * out_payload, uint16_t * out_len);

void ics2_init(void * addr, uint8_t port, uint8_t level, uint8_t speed, uint8_t mode)
{
    (void)addr;
    (void)mode;

    scope_reset();

    s_rx_head = 0U;
    s_rx_tail = 0U;
    s_tx_head = 0U;
    s_tx_tail = 0U;

    serial_configure_pins(port);
    serial_init_sci6(level, speed);
}

void ics2_watchpoint(void)
{
    protocol_process_rx();

    if (0U != s_scope.running)
    {
        if (0U == s_scope.frame_ready)
        {
            scope_sample_once();
        }

        scope_try_send_frame();
    }
}

uint32_t ics2_version(void)
{
    return 0x00010000UL;
}

void ics_int_sci_eri(void)
{
    SCI6.SSR.BIT.ORER = 0U;
    SCI6.SSR.BIT.FER  = 0U;
    SCI6.SSR.BIT.PER  = 0U;
}

void ics_int_sci_rxi(void)
{
    uint8_t data;
    uint16_t next;

    data = SCI6.RDR;

    next = (uint16_t)(s_rx_head + 1U);
    if (next >= UART_RX_BUFFER_SIZE)
    {
        next = 0U;
    }

    if (next == s_rx_tail)
    {
        uint16_t tail_next = (uint16_t)(s_rx_tail + 1U);
        if (tail_next >= UART_RX_BUFFER_SIZE)
        {
            tail_next = 0U;
        }
        s_rx_tail = tail_next;
    }

    s_rx_buffer[s_rx_head] = data;
    s_rx_head = next;
}

void ics_int_sci_txi(void)
{
    if (s_tx_head == s_tx_tail)
    {
        SCI6.SCR.BIT.TIE = 0U;
        return;
    }

    SCI6.TDR = s_tx_buffer[s_tx_tail];

    s_tx_tail++;
    if (s_tx_tail >= UART_TX_BUFFER_SIZE)
    {
        s_tx_tail = 0U;
    }
}

static void serial_configure_pins(uint8_t port)
{
    if (port != ICS_SCI6_P81_P80)
    {
        return;
    }

    R_BSP_RegisterProtectDisable(BSP_REG_PROTECT_MPC);

    PORT8.PMR.BIT.B0 = 0U;
    PORT8.PMR.BIT.B1 = 0U;
    PORT8.PDR.BIT.B0 = 0U;
    PORT8.PDR.BIT.B1 = 1U;

    MPC.P80PFS.BYTE = UART_SCI6_PFS_RX;
    MPC.P81PFS.BYTE = UART_SCI6_PFS_TX;

    PORT8.PMR.BIT.B0 = 1U;
    PORT8.PMR.BIT.B1 = 1U;

    R_BSP_RegisterProtectEnable(BSP_REG_PROTECT_MPC);
}

static void serial_init_sci6(uint8_t level, uint8_t speed)
{
    volatile uint32_t wait_count;

    IEN(SCI6, RXI6) = 0U;
    IEN(SCI6, TXI6) = 0U;
    IR(SCI6, RXI6)  = 0U;
    IR(SCI6, TXI6)  = 0U;

    MSTP(SCI6) = 0U;

    SCI6.SCR.BYTE = 0x00U;
    SCI6.SMR.BYTE = 0x00U;
    SCI6.SCMR.BYTE = 0xF2U;
    SCI6.SEMR.BYTE = 0x00U;

    /* BGDM+ABCS allows high baud rates with BRR = PCLKB/(8*baud)-1. */
    SCI6.SEMR.BIT.BGDM = 1U;
    SCI6.SEMR.BIT.ABCS = 1U;

    SCI6.BRR = speed;

    for (wait_count = 0U; wait_count < 4000U; wait_count++)
    {
        /* Delay loop after BRR update to satisfy SCI timing requirements. */
    }

    IPR(SCI6, RXI6) = level;
    IPR(SCI6, TXI6) = level;

    SCI6.SCR.BIT.RE = 1U;
    SCI6.SCR.BIT.TE = 1U;
    SCI6.SCR.BIT.RIE = 1U;
    SCI6.SCR.BIT.TIE = 0U;

    IEN(SCI6, RXI6) = 1U;
    IEN(SCI6, TXI6) = 1U;
}

static void serial_enqueue_start(void)
{
    SCI6.SCR.BIT.TIE = 1U;

    if (0U != SCI6.SSR.BIT.TDRE)
    {
        ics_int_sci_txi();
    }
}

static uint16_t tx_count(void)
{
    if (s_tx_head >= s_tx_tail)
    {
        return (uint16_t)(s_tx_head - s_tx_tail);
    }

    return (uint16_t)(UART_TX_BUFFER_SIZE - s_tx_tail + s_tx_head);
}

static uint16_t tx_free(void)
{
    return (uint16_t)(UART_TX_BUFFER_SIZE - 1U - tx_count());
}

static uint8_t tx_enqueue(const uint8_t * data, uint16_t len)
{
    uint16_t i;

    if (len > tx_free())
    {
        return 0U;
    }

    for (i = 0U; i < len; i++)
    {
        s_tx_buffer[s_tx_head] = data[i];
        s_tx_head++;
        if (s_tx_head >= UART_TX_BUFFER_SIZE)
        {
            s_tx_head = 0U;
        }
    }

    serial_enqueue_start();
    return 1U;
}

static uint16_t rx_count(void)
{
    if (s_rx_head >= s_rx_tail)
    {
        return (uint16_t)(s_rx_head - s_rx_tail);
    }

    return (uint16_t)(UART_RX_BUFFER_SIZE - s_rx_tail + s_rx_head);
}

static uint8_t rx_peek(uint16_t offset)
{
    uint16_t idx = (uint16_t)(s_rx_tail + offset);

    while (idx >= UART_RX_BUFFER_SIZE)
    {
        idx = (uint16_t)(idx - UART_RX_BUFFER_SIZE);
    }

    return s_rx_buffer[idx];
}

static void rx_drop(uint16_t count)
{
    s_rx_tail = (uint16_t)(s_rx_tail + count);
    while (s_rx_tail >= UART_RX_BUFFER_SIZE)
    {
        s_rx_tail = (uint16_t)(s_rx_tail - UART_RX_BUFFER_SIZE);
    }
}

static uint8_t var_type_size(uint8_t type)
{
    switch (type)
    {
        case UART_VAR_UINT8:
        case UART_VAR_INT8:
        case UART_VAR_BOOL:
        case UART_VAR_LOGIC:
            return 1U;

        case UART_VAR_UINT16:
        case UART_VAR_INT16:
            return 2U;

        case UART_VAR_UINT32:
        case UART_VAR_INT32:
        case UART_VAR_FLOAT32:
            return 4U;

        default:
            return 0U;
    }
}

static uint8_t var_region_contains(uint32_t addr, uint8_t size, uint32_t start, uint32_t end)
{
    uint32_t limit;

    if ((0U == size) || (addr < start))
    {
        return 0U;
    }

    limit = addr + (uint32_t)size;
    if (limit < addr)
    {
        return 0U;
    }

    return (uint8_t)((limit <= end) ? 1U : 0U);
}

static uint8_t var_address_is_valid(uint32_t addr, uint8_t size)
{
    if ((size == 2U) && ((addr & 0x1U) != 0U))
    {
        return 0U;
    }

    if ((size == 4U) && ((addr & 0x3U) != 0U))
    {
        return 0U;
    }

    if (0U != var_region_contains(addr, size, UART_RAM_REGION0_START, UART_RAM_REGION0_END))
    {
        return 1U;
    }

    if (0U != var_region_contains(addr, size, UART_RAM_REGION1_START, UART_RAM_REGION1_END))
    {
        return 1U;
    }

    return 0U;
}

static uint8_t var_read_value(st_uart_var_ref_t var_ref, uint8_t * out_data)
{
    union
    {
        uint16_t u16;
        int16_t  i16;
        uint32_t u32;
        int32_t  i32;
        float    f32;
        uint8_t  b[4];
    } conv;
    uint8_t size;

    if (out_data == 0)
    {
        return 0U;
    }

    size = var_type_size(var_ref.type);
    if ((0U == size) || (0U == var_address_is_valid(var_ref.addr, size)))
    {
        return 0U;
    }

    switch (var_ref.type)
    {
        case UART_VAR_UINT8:
        case UART_VAR_BOOL:
        case UART_VAR_LOGIC:
            out_data[0] = *((volatile uint8_t *)(unsigned long)var_ref.addr);
            return 1U;

        case UART_VAR_INT8:
            out_data[0] = (uint8_t)(*((volatile int8_t *)(unsigned long)var_ref.addr));
            return 1U;

        case UART_VAR_UINT16:
            conv.u16 = *((volatile uint16_t *)(unsigned long)var_ref.addr);
            out_data[0] = conv.b[0];
            out_data[1] = conv.b[1];
            return 2U;

        case UART_VAR_INT16:
            conv.i16 = *((volatile int16_t *)(unsigned long)var_ref.addr);
            out_data[0] = conv.b[0];
            out_data[1] = conv.b[1];
            return 2U;

        case UART_VAR_UINT32:
            conv.u32 = *((volatile uint32_t *)(unsigned long)var_ref.addr);
            out_data[0] = conv.b[0];
            out_data[1] = conv.b[1];
            out_data[2] = conv.b[2];
            out_data[3] = conv.b[3];
            return 4U;

        case UART_VAR_INT32:
            conv.i32 = *((volatile int32_t *)(unsigned long)var_ref.addr);
            out_data[0] = conv.b[0];
            out_data[1] = conv.b[1];
            out_data[2] = conv.b[2];
            out_data[3] = conv.b[3];
            return 4U;

        case UART_VAR_FLOAT32:
            conv.f32 = *((volatile float *)(unsigned long)var_ref.addr);
            out_data[0] = conv.b[0];
            out_data[1] = conv.b[1];
            out_data[2] = conv.b[2];
            out_data[3] = conv.b[3];
            return 4U;

        default:
            return 0U;
    }
}

static uint8_t var_write_value(st_uart_var_ref_t var_ref, const uint8_t * in_data, uint16_t len)
{
    union
    {
        uint16_t u16;
        int16_t  i16;
        uint32_t u32;
        int32_t  i32;
        float    f32;
        uint8_t  b[4];
    } conv;
    uint8_t size;

    if (in_data == 0)
    {
        return 0U;
    }

    size = var_type_size(var_ref.type);
    if ((0U == size) || (len < size) || (0U == var_address_is_valid(var_ref.addr, size)))
    {
        return 0U;
    }

    switch (var_ref.type)
    {
        case UART_VAR_UINT8:
        case UART_VAR_BOOL:
        case UART_VAR_LOGIC:
            *((volatile uint8_t *)(unsigned long)var_ref.addr) = in_data[0];
            return 1U;

        case UART_VAR_INT8:
            *((volatile int8_t *)(unsigned long)var_ref.addr) = (int8_t)in_data[0];
            return 1U;

        case UART_VAR_UINT16:
            conv.b[0] = in_data[0];
            conv.b[1] = in_data[1];
            *((volatile uint16_t *)(unsigned long)var_ref.addr) = conv.u16;
            return 1U;

        case UART_VAR_INT16:
            conv.b[0] = in_data[0];
            conv.b[1] = in_data[1];
            *((volatile int16_t *)(unsigned long)var_ref.addr) = conv.i16;
            return 1U;

        case UART_VAR_UINT32:
            conv.b[0] = in_data[0];
            conv.b[1] = in_data[1];
            conv.b[2] = in_data[2];
            conv.b[3] = in_data[3];
            *((volatile uint32_t *)(unsigned long)var_ref.addr) = conv.u32;
            return 1U;

        case UART_VAR_INT32:
            conv.b[0] = in_data[0];
            conv.b[1] = in_data[1];
            conv.b[2] = in_data[2];
            conv.b[3] = in_data[3];
            *((volatile int32_t *)(unsigned long)var_ref.addr) = conv.i32;
            return 1U;

        case UART_VAR_FLOAT32:
            conv.b[0] = in_data[0];
            conv.b[1] = in_data[1];
            conv.b[2] = in_data[2];
            conv.b[3] = in_data[3];
            *((volatile float *)(unsigned long)var_ref.addr) = conv.f32;
            return 1U;

        default:
            return 0U;
    }
}

static float var_read_as_float(st_uart_var_ref_t var_ref, uint8_t * ok)
{
    union
    {
        uint16_t u16;
        int16_t  i16;
        uint32_t u32;
        int32_t  i32;
        float    f32;
        uint8_t  b[4];
    } conv;
    uint8_t raw[4];
    uint8_t len;

    len = var_read_value(var_ref, raw);
    if (0U == len)
    {
        if (ok != 0)
        {
            *ok = 0U;
        }
        return 0.0f;
    }

    if (ok != 0)
    {
        *ok = 1U;
    }

    switch (var_ref.type)
    {
        case UART_VAR_UINT8:
            return (float)raw[0];

        case UART_VAR_INT8:
            return (float)((int8_t)raw[0]);

        case UART_VAR_UINT16:
            conv.b[0] = raw[0];
            conv.b[1] = raw[1];
            return (float)conv.u16;

        case UART_VAR_INT16:
            conv.b[0] = raw[0];
            conv.b[1] = raw[1];
            return (float)conv.i16;

        case UART_VAR_UINT32:
            conv.b[0] = raw[0];
            conv.b[1] = raw[1];
            conv.b[2] = raw[2];
            conv.b[3] = raw[3];
            return (float)conv.u32;

        case UART_VAR_INT32:
            conv.b[0] = raw[0];
            conv.b[1] = raw[1];
            conv.b[2] = raw[2];
            conv.b[3] = raw[3];
            return (float)conv.i32;

        case UART_VAR_FLOAT32:
            conv.b[0] = raw[0];
            conv.b[1] = raw[1];
            conv.b[2] = raw[2];
            conv.b[3] = raw[3];
            return conv.f32;

        case UART_VAR_BOOL:
        case UART_VAR_LOGIC:
            return (raw[0] != 0U) ? 1.0f : 0.0f;

        default:
            if (ok != 0)
            {
                *ok = 0U;
            }
            return 0.0f;
    }
}

static uint8_t protocol_send_packet(uint8_t command, const uint8_t * payload, uint16_t len)
{
    uint8_t header[5];
    uint8_t checksum = command;
    uint8_t checksum_tail;
    uint16_t i;

    if (len > UART_MAX_TX_PAYLOAD)
    {
        return 0U;
    }

    if (tx_free() < (uint16_t)(len + 6U))
    {
        return 0U;
    }

    header[0] = UART_SYNC_0;
    header[1] = UART_SYNC_1;
    header[2] = command;
    header[3] = (uint8_t)(len & 0xFFU);
    header[4] = (uint8_t)((len >> 8U) & 0xFFU);

    checksum = (uint8_t)(checksum + header[3]);
    checksum = (uint8_t)(checksum + header[4]);

    for (i = 0U; i < len; i++)
    {
        checksum = (uint8_t)(checksum + payload[i]);
    }

    checksum_tail = checksum;

    if (0U == tx_enqueue(header, 5U))
    {
        return 0U;
    }

    if ((len > 0U) && (0U == tx_enqueue(payload, len)))
    {
        return 0U;
    }

    if (0U == tx_enqueue(&checksum_tail, 1U))
    {
        return 0U;
    }

    return 1U;
}

static void protocol_send_ack(void)
{
    (void)protocol_send_packet(UART_CMD_ACK, 0, 0U);
}

static void protocol_send_nack(void)
{
    (void)protocol_send_packet(UART_CMD_NACK, 0, 0U);
}

static void protocol_process_rx(void)
{
    uint16_t available;

    while (1)
    {
        uint8_t command;
        uint16_t len;
        uint16_t total;
        uint8_t checksum;
        uint8_t expected;
        uint16_t i;

        available = rx_count();
        if (available < 6U)
        {
            return;
        }

        while (available >= 2U)
        {
            if ((rx_peek(0U) == UART_SYNC_0) && (rx_peek(1U) == UART_SYNC_1))
            {
                break;
            }

            rx_drop(1U);
            available = rx_count();
        }

        if (available < 6U)
        {
            return;
        }

        command = rx_peek(2U);
        len = (uint16_t)rx_peek(3U);
        len = (uint16_t)(len | ((uint16_t)rx_peek(4U) << 8U));

        if (len > UART_MAX_RX_PAYLOAD)
        {
            rx_drop(1U);
            continue;
        }

        total = (uint16_t)(len + 6U);
        if (available < total)
        {
            return;
        }

        checksum = 0U;
        for (i = 2U; i < (uint16_t)(5U + len); i++)
        {
            checksum = (uint8_t)(checksum + rx_peek(i));
        }

        expected = rx_peek((uint16_t)(5U + len));
        if (checksum != expected)
        {
            rx_drop(1U);
            continue;
        }

        for (i = 0U; i < len; i++)
        {
            s_payload_buffer[i] = rx_peek((uint16_t)(5U + i));
        }

        rx_drop(total);
        protocol_handle_command(command, s_payload_buffer, len);
    }
}

static void protocol_handle_command(uint8_t command, const uint8_t * payload, uint16_t len)
{
    switch (command)
    {
        case UART_CMD_PING:
            protocol_send_ack();
            break;

        case UART_CMD_GET_INFO:
            protocol_handle_get_info();
            break;

        case UART_CMD_READ_VARIABLE:
            protocol_handle_read_variable(payload, len);
            break;

        case UART_CMD_WRITE_VARIABLE:
            protocol_handle_write_variable(payload, len);
            break;

        case UART_CMD_START_SCOPE:
            protocol_handle_start_scope(payload, len);
            break;

        case UART_CMD_STOP_SCOPE:
            protocol_handle_stop_scope();
            break;

        case UART_CMD_SET_TRIGGER:
            protocol_handle_set_trigger(payload, len);
            break;

        case UART_CMD_SET_CHANNELS:
            protocol_handle_set_channels(payload, len);
            break;

        case UART_CMD_SET_SAMPLING:
            protocol_handle_set_sampling(payload, len);
            break;

        default:
            protocol_send_nack();
            break;
    }
}

static void protocol_handle_get_info(void)
{
    static const char cpu_name[] = "RX26T";
    static const char lib_name[] = "MCUScope-UART-1.0";
    union
    {
        float f32;
        uint8_t b[4];
    } conv;
    uint8_t payload[64];
    uint8_t idx = 0U;
    uint8_t i;

    conv.f32 = 40.0f;

    payload[idx++] = (uint8_t)(sizeof(cpu_name) - 1U);
    for (i = 0U; i < (uint8_t)(sizeof(cpu_name) - 1U); i++)
    {
        payload[idx++] = (uint8_t)cpu_name[i];
    }

    payload[idx++] = (uint8_t)(sizeof(lib_name) - 1U);
    for (i = 0U; i < (uint8_t)(sizeof(lib_name) - 1U); i++)
    {
        payload[idx++] = (uint8_t)lib_name[i];
    }

    payload[idx++] = conv.b[0];
    payload[idx++] = conv.b[1];
    payload[idx++] = conv.b[2];
    payload[idx++] = conv.b[3];

    (void)protocol_send_packet(UART_CMD_INFO_RESPONSE, payload, idx);
}
static void protocol_handle_read_variable(const uint8_t * payload, uint16_t len)
{
    uint8_t name_len;
    uint16_t offset;
    uint32_t addr;
    uint8_t type;
    st_uart_var_ref_t var_ref;
    uint8_t payload_out[96];
    uint8_t value_bytes[4];
    uint8_t value_len;
    uint16_t i;

    if (len < 6U)
    {
        protocol_send_nack();
        return;
    }

    name_len = payload[0];
    if (len < (uint16_t)(1U + name_len + 5U))
    {
        protocol_send_nack();
        return;
    }

    offset = (uint16_t)(1U + name_len);
    addr = (uint32_t)payload[offset];
    addr |= ((uint32_t)payload[(uint16_t)(offset + 1U)] << 8U);
    addr |= ((uint32_t)payload[(uint16_t)(offset + 2U)] << 16U);
    addr |= ((uint32_t)payload[(uint16_t)(offset + 3U)] << 24U);
    type = payload[(uint16_t)(offset + 4U)];

    var_ref.addr = addr;
    var_ref.type = type;

    value_len = var_read_value(var_ref, value_bytes);
    if (0U == value_len)
    {
        protocol_send_nack();
        return;
    }

    if ((uint16_t)(2U + name_len + value_len) > (uint16_t)sizeof(payload_out))
    {
        protocol_send_nack();
        return;
    }

    payload_out[0] = name_len;
    for (i = 0U; i < name_len; i++)
    {
        payload_out[(uint16_t)(1U + i)] = payload[(uint16_t)(1U + i)];
    }

    payload_out[(uint16_t)(1U + name_len)] = type;
    for (i = 0U; i < value_len; i++)
    {
        payload_out[(uint16_t)(2U + name_len + i)] = value_bytes[i];
    }

    (void)protocol_send_packet(UART_CMD_VARIABLE_DATA, payload_out, (uint16_t)(2U + name_len + value_len));
}

static void protocol_handle_write_variable(const uint8_t * payload, uint16_t len)
{
    uint8_t name_len;
    uint16_t offset;
    uint32_t addr;
    uint8_t type;
    st_uart_var_ref_t var_ref;

    if (len < 7U)
    {
        protocol_send_nack();
        return;
    }

    name_len = payload[0];
    if (len < (uint16_t)(1U + name_len + 5U))
    {
        protocol_send_nack();
        return;
    }

    offset = (uint16_t)(1U + name_len);
    addr = (uint32_t)payload[offset];
    addr |= ((uint32_t)payload[(uint16_t)(offset + 1U)] << 8U);
    addr |= ((uint32_t)payload[(uint16_t)(offset + 2U)] << 16U);
    addr |= ((uint32_t)payload[(uint16_t)(offset + 3U)] << 24U);
    type = payload[(uint16_t)(offset + 4U)];

    var_ref.addr = addr;
    var_ref.type = type;

    if (0U == var_write_value(var_ref, &payload[(uint16_t)(offset + 5U)], (uint16_t)(len - offset - 5U)))
    {
        protocol_send_nack();
        return;
    }

    protocol_send_ack();
}

static void protocol_handle_start_scope(const uint8_t * payload, uint16_t len)
{
    union
    {
        float f32;
        uint8_t b[4];
    } conv;
    int32_t record_len;
    uint8_t channel_count_raw;
    uint8_t channel_count;
    uint8_t i;
    uint16_t offset;
    uint8_t valid_count = 0U;

    if (len < 9U)
    {
        protocol_send_nack();
        return;
    }

    conv.b[0] = payload[0];
    conv.b[1] = payload[1];
    conv.b[2] = payload[2];
    conv.b[3] = payload[3];

    record_len = (int32_t)payload[4];
    record_len |= ((int32_t)payload[5] << 8);
    record_len |= ((int32_t)payload[6] << 16);
    record_len |= ((int32_t)payload[7] << 24);

    channel_count_raw = payload[8];
    if (len < (uint16_t)(9U + ((uint16_t)channel_count_raw * UART_SCOPE_DYNAMIC_ENTRY_SIZE)))
    {
        protocol_send_nack();
        return;
    }

    if (record_len < 8)
    {
        record_len = 8;
    }
    else if (record_len > (int32_t)UART_MAX_SCOPE_RECORD_LENGTH)
    {
        record_len = (int32_t)UART_MAX_SCOPE_RECORD_LENGTH;
    }

    s_scope.sample_period = conv.f32;
    s_scope.record_length = (uint16_t)record_len;

    channel_count = channel_count_raw;
    if (channel_count > UART_MAX_SCOPE_CHANNELS)
    {
        channel_count = UART_MAX_SCOPE_CHANNELS;
    }

    offset = 9U;
    for (i = 0U; i < channel_count; i++)
    {
        st_uart_scope_channel_t channel;
        uint8_t size;

        channel.slot = payload[offset];
        channel.type = payload[(uint16_t)(offset + 1U)];
        channel.addr = (uint32_t)payload[(uint16_t)(offset + 2U)];
        channel.addr |= ((uint32_t)payload[(uint16_t)(offset + 3U)] << 8U);
        channel.addr |= ((uint32_t)payload[(uint16_t)(offset + 4U)] << 16U);
        channel.addr |= ((uint32_t)payload[(uint16_t)(offset + 5U)] << 24U);
        offset = (uint16_t)(offset + UART_SCOPE_DYNAMIC_ENTRY_SIZE);

        size = var_type_size(channel.type);
        if ((channel.slot < UART_MAX_SCOPE_CHANNELS) && (0U != size) &&
            (0U != var_address_is_valid(channel.addr, size)))
        {
            s_scope.channels[valid_count] = channel;
            valid_count++;
        }
    }

    if (0U == valid_count)
    {
        protocol_send_nack();
        return;
    }

    s_scope.channel_count = valid_count;
    s_scope.sample_count = 0U;
    s_scope.frame_ready = 0U;
    s_scope.send_channel_cursor = 0U;
    s_scope.running = 1U;

    protocol_send_ack();
}

static void protocol_handle_stop_scope(void)
{
    s_scope.running = 0U;
    s_scope.frame_ready = 0U;
    s_scope.sample_count = 0U;
    s_scope.send_channel_cursor = 0U;

    protocol_send_ack();
}

static void protocol_handle_set_trigger(const uint8_t * payload, uint16_t len)
{
    union
    {
        float f32;
        uint8_t b[4];
    } conv;

    if (len < 11U)
    {
        protocol_send_nack();
        return;
    }

    conv.b[0] = payload[0];
    conv.b[1] = payload[1];
    conv.b[2] = payload[2];
    conv.b[3] = payload[3];
    s_scope.trigger_position = conv.f32;

    conv.b[0] = payload[4];
    conv.b[1] = payload[5];
    conv.b[2] = payload[6];
    conv.b[3] = payload[7];
    s_scope.trigger_level = conv.f32;

    s_scope.trigger_source = payload[8];
    s_scope.trigger_mode = payload[9];
    s_scope.trigger_edge = payload[10];

    protocol_send_ack();
}

static void protocol_handle_set_channels(const uint8_t * payload, uint16_t len)
{
    uint8_t count_raw;
    uint8_t count;
    uint8_t i;
    uint16_t offset;
    uint8_t valid_count = 0U;

    if (len < 1U)
    {
        protocol_send_nack();
        return;
    }

    count_raw = payload[0];
    if (len < (uint16_t)(1U + ((uint16_t)count_raw * UART_SCOPE_DYNAMIC_ENTRY_SIZE)))
    {
        protocol_send_nack();
        return;
    }

    count = count_raw;
    if (count > UART_MAX_SCOPE_CHANNELS)
    {
        count = UART_MAX_SCOPE_CHANNELS;
    }

    offset = 1U;
    for (i = 0U; i < count; i++)
    {
        st_uart_scope_channel_t channel;
        uint8_t size;

        channel.slot = payload[offset];
        channel.type = payload[(uint16_t)(offset + 1U)];
        channel.addr = (uint32_t)payload[(uint16_t)(offset + 2U)];
        channel.addr |= ((uint32_t)payload[(uint16_t)(offset + 3U)] << 8U);
        channel.addr |= ((uint32_t)payload[(uint16_t)(offset + 4U)] << 16U);
        channel.addr |= ((uint32_t)payload[(uint16_t)(offset + 5U)] << 24U);
        offset = (uint16_t)(offset + UART_SCOPE_DYNAMIC_ENTRY_SIZE);

        size = var_type_size(channel.type);
        if ((channel.slot < UART_MAX_SCOPE_CHANNELS) && (0U != size) &&
            (0U != var_address_is_valid(channel.addr, size)))
        {
            s_scope.channels[valid_count] = channel;
            valid_count++;
        }
    }

    if (0U == valid_count)
    {
        protocol_send_nack();
        return;
    }

    s_scope.channel_count = valid_count;
    s_scope.sample_count = 0U;
    s_scope.frame_ready = 0U;
    s_scope.send_channel_cursor = 0U;

    protocol_send_ack();
}

static void protocol_handle_set_sampling(const uint8_t * payload, uint16_t len)
{
    union
    {
        float f32;
        uint8_t b[4];
    } conv;
    uint16_t record_len;

    if (len < 6U)
    {
        protocol_send_nack();
        return;
    }

    conv.b[0] = payload[0];
    conv.b[1] = payload[1];
    conv.b[2] = payload[2];
    conv.b[3] = payload[3];

    record_len = (uint16_t)payload[4];
    record_len = (uint16_t)(record_len | ((uint16_t)payload[5] << 8U));

    if (record_len < 8U)
    {
        record_len = 8U;
    }
    else if (record_len > UART_MAX_SCOPE_RECORD_LENGTH)
    {
        record_len = UART_MAX_SCOPE_RECORD_LENGTH;
    }

    s_scope.sample_period = conv.f32;
    s_scope.record_length = record_len;

    protocol_send_ack();
}

static void scope_reset(void)
{
    memset(&s_scope, 0, sizeof(s_scope));

    s_scope.sample_period = 0.0001f;
    s_scope.record_length = 100U;
    s_scope.channel_count = 0U;
}

static float scope_read_channel_value(const st_uart_scope_channel_t * p_channel, uint8_t * ok)
{
    st_uart_var_ref_t var_ref;

    if (p_channel == 0)
    {
        if (ok != 0)
        {
            *ok = 0U;
        }
        return 0.0f;
    }

    var_ref.addr = p_channel->addr;
    var_ref.type = p_channel->type;
    return var_read_as_float(var_ref, ok);
}

static void scope_sample_once(void)
{
    uint8_t i;

    if (s_scope.sample_count >= s_scope.record_length)
    {
        s_scope.frame_ready = 1U;
        s_scope.send_channel_cursor = 0U;
        return;
    }

    for (i = 0U; i < s_scope.channel_count; i++)
    {
        uint8_t valid;
        s_scope.buffer[i][s_scope.sample_count] = scope_read_channel_value(&s_scope.channels[i], &valid);
        if (0U == valid)
        {
            s_scope.buffer[i][s_scope.sample_count] = 0.0f;
        }
    }

    s_scope.sample_count++;

    if (s_scope.sample_count >= s_scope.record_length)
    {
        s_scope.frame_ready = 1U;
        s_scope.send_channel_cursor = 0U;
    }
}

static void scope_try_send_frame(void)
{
    uint8_t channel_order;
    uint16_t payload_len;

    if (0U == s_scope.frame_ready)
    {
        return;
    }

    if (s_scope.send_channel_cursor >= s_scope.channel_count)
    {
        s_scope.frame_ready = 0U;
        s_scope.sample_count = 0U;
        s_scope.send_channel_cursor = 0U;
        return;
    }

    channel_order = s_scope.send_channel_cursor;

    if (0U == scope_build_waveform_payload(channel_order, s_payload_buffer, &payload_len))
    {
        return;
    }

    if (0U != protocol_send_packet(UART_CMD_WAVEFORM_DATA, s_payload_buffer, payload_len))
    {
        s_scope.send_channel_cursor++;
    }
}

static uint8_t scope_build_waveform_payload(uint8_t channel_order, uint8_t * out_payload, uint16_t * out_len)
{
    union
    {
        float f32;
        uint8_t b[4];
    } conv;
    uint16_t idx;
    uint16_t i;

    if ((out_payload == 0) || (out_len == 0) || (channel_order >= s_scope.channel_count))
    {
        return 0U;
    }

    idx = 0U;
    out_payload[idx++] = s_scope.channels[channel_order].slot;

    conv.f32 = s_scope.sample_period;
    out_payload[idx++] = conv.b[0];
    out_payload[idx++] = conv.b[1];
    out_payload[idx++] = conv.b[2];
    out_payload[idx++] = conv.b[3];

    out_payload[idx++] = (uint8_t)(s_scope.record_length & 0xFFU);
    out_payload[idx++] = (uint8_t)((s_scope.record_length >> 8U) & 0xFFU);

    for (i = 0U; i < s_scope.record_length; i++)
    {
        conv.f32 = s_scope.buffer[channel_order][i];
        out_payload[idx++] = conv.b[0];
        out_payload[idx++] = conv.b[1];
        out_payload[idx++] = conv.b[2];
        out_payload[idx++] = conv.b[3];
    }

    *out_len = idx;
    return 1U;
}
