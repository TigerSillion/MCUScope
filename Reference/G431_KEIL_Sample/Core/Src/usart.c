/* USER CODE BEGIN Header */
/**
  ******************************************************************************
  * @file    usart.c
  * @brief   This file provides code for the configuration
  *          of the USART instances.
  ******************************************************************************
  * @attention
  *
  * Copyright (c) 2026 STMicroelectronics.
  * All rights reserved.
  *
  * This software is licensed under terms that can be found in the LICENSE file
  * in the root directory of this software component.
  * If no LICENSE file comes with this software, it is provided AS-IS.
  *
  ******************************************************************************
  */
/* USER CODE END Header */
/* Includes ------------------------------------------------------------------*/
#include "usart.h"

/* USER CODE BEGIN 0 */
#include "serialDriver.h"
#include "motor_sim_vars.h"
#include <string.h>

#define USE_ICS2_PROTOCOL        1
#define SERIAL_DRIVER_UART_SELF_TEST 0
#define SERIAL_DRIVER_TX_QUEUE_DEPTH 4U
#define SERIAL_DRIVER_TX_MAX_SIZE    768U

#define ICS2_SYNC1                    0xAAU
#define ICS2_SYNC2                    0x55U
#define ICS2_CMD_PING                 0x01U
#define ICS2_CMD_GET_INFO             0x02U
#define ICS2_CMD_READ_VARIABLE        0x10U
#define ICS2_CMD_WRITE_VARIABLE       0x11U
#define ICS2_CMD_START_SCOPE          0x20U
#define ICS2_CMD_STOP_SCOPE           0x21U
#define ICS2_CMD_SET_TRIGGER          0x24U
#define ICS2_CMD_ACK                  0x80U
#define ICS2_CMD_NACK                 0x81U
#define ICS2_CMD_INFO_RESPONSE        0x82U
#define ICS2_CMD_VARIABLE_DATA        0x83U
#define ICS2_CMD_WAVEFORM_DATA        0x84U

#define ICS2_MAX_PAYLOAD_SIZE         192U
#define ICS2_MAX_SCOPE_CHANNELS       12U
#define ICS2_MAX_SCOPE_RECORD_LENGTH  128U
#define ICS2_NACK_UNKNOWN             0x00U
#define ICS2_NACK_INVALID_PAYLOAD     0x01U
#define ICS2_NACK_UNSUPPORTED_CMD     0x02U
#define ICS2_NACK_BAD_VARIABLE        0x03U
#define ICS2_NACK_WRITE_DENIED        0x04U
#define ICS2_NACK_BAD_SCOPE_CONFIG    0x05U
#define ICS2_NACK_BAD_TRIGGER_CONFIG  0x06U

typedef enum
{
  Ics2StateWaitSync1 = 0,
  Ics2StateWaitSync2,
  Ics2StateCmd,
  Ics2StateLenL,
  Ics2StateLenH,
  Ics2StatePayload,
  Ics2StateChecksum
} Ics2RxState_t;

typedef enum
{
  Ics2VarTypeU8 = 0,
  Ics2VarTypeI8 = 1,
  Ics2VarTypeU16 = 2,
  Ics2VarTypeI16 = 3,
  Ics2VarTypeU32 = 4,
  Ics2VarTypeI32 = 5,
  Ics2VarTypeF32 = 6,
  Ics2VarTypeBool = 7,
  Ics2VarTypeLogic = 8
} Ics2VarType_t;

typedef struct
{
  const char *name;
  uint32_t address;
  uint8_t type;
  uint8_t writable;
} Ics2VarEntry_t;

typedef struct
{
  uint8_t slot;
  uint8_t type;
  const Ics2VarEntry_t *entry;
} Ics2ScopeChannel_t;

typedef struct
{
  uint8_t running;
  float sample_period_s;
  uint32_t sample_period_ms;
  int32_t record_length;
  uint8_t channel_count;
  uint32_t next_sample_tick;
  int32_t sample_index;
  Ics2ScopeChannel_t channels[ICS2_MAX_SCOPE_CHANNELS];
  float sample_data[ICS2_MAX_SCOPE_CHANNELS][ICS2_MAX_SCOPE_RECORD_LENGTH];
} Ics2ScopeState_t;

typedef struct
{
  float position;
  float level;
  uint8_t source;
  uint8_t mode;
  uint8_t edge;
} Ics2Trigger_t;

static uint8_t g_lpuart1RxByte = 0U;
static uint8_t g_lpuart1TxQueue[SERIAL_DRIVER_TX_QUEUE_DEPTH][SERIAL_DRIVER_TX_MAX_SIZE];
static uint16_t g_lpuart1TxSize[SERIAL_DRIVER_TX_QUEUE_DEPTH];
static volatile uint8_t g_lpuart1TxHead = 0U;
static volatile uint8_t g_lpuart1TxTail = 0U;
static volatile uint8_t g_lpuart1TxCount = 0U;
static uint8_t g_ics2TxFrame[SERIAL_DRIVER_TX_MAX_SIZE];
static Ics2ScopeState_t g_ics2Scope = {0};
static Ics2Trigger_t g_ics2Trigger = {0};
static Ics2RxState_t g_ics2RxState = Ics2StateWaitSync1;
static uint8_t g_ics2RxCmd = 0U;
static uint16_t g_ics2RxLen = 0U;
static uint16_t g_ics2RxIndex = 0U;
static uint8_t g_ics2RxPayload[ICS2_MAX_PAYLOAD_SIZE];
static uint8_t g_ics2RxChecksum = 0U;

static const Ics2VarEntry_t g_ics2VarTable[] =
{
  {"com_u1_system_mode", (uint32_t)(uintptr_t)&com_u1_system_mode, Ics2VarTypeU8, 1U},
  {"g_u1_system_mode", (uint32_t)(uintptr_t)&g_u1_system_mode, Ics2VarTypeU8, 0U},
  {"com_f4_ref_speed_rpm", (uint32_t)(uintptr_t)&com_f4_ref_speed_rpm, Ics2VarTypeF32, 1U},
  {"com_f4_speed_rate_limit_rpm", (uint32_t)(uintptr_t)&com_f4_speed_rate_limit_rpm, Ics2VarTypeF32, 1U},
  {"g_st_sensorless_vector.f4_vdc_ad", (uint32_t)(uintptr_t)&g_st_sensorless_vector.f4_vdc_ad, Ics2VarTypeF32, 0U},
  {"g_st_sensorless_vector.f4_iu_ad", (uint32_t)(uintptr_t)&g_st_sensorless_vector.f4_iu_ad, Ics2VarTypeF32, 0U},
  {"g_st_sensorless_vector.f4_iv_ad", (uint32_t)(uintptr_t)&g_st_sensorless_vector.f4_iv_ad, Ics2VarTypeF32, 0U},
  {"g_st_sensorless_vector.f4_iw_ad", (uint32_t)(uintptr_t)&g_st_sensorless_vector.f4_iw_ad, Ics2VarTypeF32, 0U},
  {"g_st_sensorless_vector.st_speed_output.f4_speed_rad_lpf", (uint32_t)(uintptr_t)&g_st_sensorless_vector.st_speed_output.f4_speed_rad_lpf, Ics2VarTypeF32, 0U},
  {"g_st_sensorless_vector.st_speed_output.f4_ref_speed_rad_ctrl", (uint32_t)(uintptr_t)&g_st_sensorless_vector.st_speed_output.f4_ref_speed_rad_ctrl, Ics2VarTypeF32, 0U},
  {"g_st_sensorless_vector.st_speed_output.f4_id_ref", (uint32_t)(uintptr_t)&g_st_sensorless_vector.st_speed_output.f4_id_ref, Ics2VarTypeF32, 0U},
  {"g_st_sensorless_vector.st_speed_output.f4_iq_ref", (uint32_t)(uintptr_t)&g_st_sensorless_vector.st_speed_output.f4_iq_ref, Ics2VarTypeF32, 0U},
  {"g_st_sensorless_vector.st_current_output.f4_ref_id_ctrl", (uint32_t)(uintptr_t)&g_st_sensorless_vector.st_current_output.f4_ref_id_ctrl, Ics2VarTypeF32, 0U},
  {"g_st_sensorless_vector.st_current_output.f4_speed_rad", (uint32_t)(uintptr_t)&g_st_sensorless_vector.st_current_output.f4_speed_rad, Ics2VarTypeF32, 0U},
  {"g_st_sensorless_vector.st_current_output.f4_ed", (uint32_t)(uintptr_t)&g_st_sensorless_vector.st_current_output.f4_ed, Ics2VarTypeF32, 0U},
  {"g_st_sensorless_vector.st_current_output.f4_eq", (uint32_t)(uintptr_t)&g_st_sensorless_vector.st_current_output.f4_eq, Ics2VarTypeF32, 0U},
  {"g_st_sensorless_vector.st_current_output.f4_phase_err_rad", (uint32_t)(uintptr_t)&g_st_sensorless_vector.st_current_output.f4_phase_err_rad, Ics2VarTypeF32, 0U},
  {"g_st_sensorless_vector.st_stm.u1_status", (uint32_t)(uintptr_t)&g_st_sensorless_vector.st_stm.u1_status, Ics2VarTypeU8, 0U}
};

static void LPUART1_StartRxDma(void);
static void LPUART1_StartNextTxDma(void);
static void LPUART1_QueueTxData(const uint8_t* data, uint16_t size);
static uint8_t ICS2_CalcChecksum(uint8_t cmd, uint16_t len, const uint8_t *payload);
static void ICS2_SendFrame(uint8_t cmd, const uint8_t *payload, uint16_t len);
static void ICS2_SendAck(void);
static void ICS2_SendNack(void);
static void ICS2_SendNackWithReason(uint8_t reason);
static uint8_t ICS2_GetTypeSize(uint8_t type);
static const Ics2VarEntry_t *ICS2_FindVariable(uint32_t address, uint8_t type);
static uint8_t ICS2_ReadVariableRaw(const Ics2VarEntry_t *entry, uint8_t *out_raw);
static uint8_t ICS2_WriteVariableRaw(const Ics2VarEntry_t *entry, const uint8_t *raw, uint8_t raw_size);
static float ICS2_ReadVariableAsFloat(const Ics2VarEntry_t *entry);
static void ICS2_HandleGetInfo(void);
static void ICS2_HandleReadVariable(const uint8_t *payload, uint16_t len);
static void ICS2_HandleWriteVariable(const uint8_t *payload, uint16_t len);
static void ICS2_HandleStartScope(const uint8_t *payload, uint16_t len);
static void ICS2_HandleStopScope(void);
static void ICS2_HandleSetTrigger(const uint8_t *payload, uint16_t len);
static void ICS2_HandleFrame(uint8_t cmd, const uint8_t *payload, uint16_t len);
static void ICS2_OnByteReceived(uint8_t byte);
static void ICS2_StreamWaveform(void);

/* USER CODE END 0 */

UART_HandleTypeDef hlpuart1;
DMA_HandleTypeDef hdma_lpuart1_rx;
DMA_HandleTypeDef hdma_lpuart1_tx;

/* LPUART1 init function */

void MX_LPUART1_UART_Init(void)
{

  /* USER CODE BEGIN LPUART1_Init 0 */

  /* USER CODE END LPUART1_Init 0 */

  /* USER CODE BEGIN LPUART1_Init 1 */

  /* USER CODE END LPUART1_Init 1 */
  hlpuart1.Instance = LPUART1;
  hlpuart1.Init.BaudRate = 2000000;
  hlpuart1.Init.WordLength = UART_WORDLENGTH_8B;
  hlpuart1.Init.StopBits = UART_STOPBITS_1;
  hlpuart1.Init.Parity = UART_PARITY_NONE;
  hlpuart1.Init.Mode = UART_MODE_TX_RX;
  hlpuart1.Init.HwFlowCtl = UART_HWCONTROL_NONE;
  hlpuart1.Init.OneBitSampling = UART_ONE_BIT_SAMPLE_DISABLE;
  hlpuart1.Init.ClockPrescaler = UART_PRESCALER_DIV1;
  hlpuart1.AdvancedInit.AdvFeatureInit = UART_ADVFEATURE_NO_INIT;
  if (HAL_UART_Init(&hlpuart1) != HAL_OK)
  {
    Error_Handler();
  }
  if (HAL_UARTEx_SetTxFifoThreshold(&hlpuart1, UART_TXFIFO_THRESHOLD_1_8) != HAL_OK)
  {
    Error_Handler();
  }
  if (HAL_UARTEx_SetRxFifoThreshold(&hlpuart1, UART_RXFIFO_THRESHOLD_1_8) != HAL_OK)
  {
    Error_Handler();
  }
  if (HAL_UARTEx_DisableFifoMode(&hlpuart1) != HAL_OK)
  {
    Error_Handler();
  }
  /* USER CODE BEGIN LPUART1_Init 2 */
  LPUART1_StartRxDma();
#if SERIAL_DRIVER_UART_SELF_TEST
  {
    uint8_t ready_msg[] = "LPUART1 ready, echo mode on\r\n";
    LPUART1_QueueTxData(ready_msg, (uint16_t)(sizeof(ready_msg) - 1U));
  }
#endif

  /* USER CODE END LPUART1_Init 2 */

}

void HAL_UART_MspInit(UART_HandleTypeDef* uartHandle)
{

  GPIO_InitTypeDef GPIO_InitStruct = {0};
  RCC_PeriphCLKInitTypeDef PeriphClkInit = {0};
  if(uartHandle->Instance==LPUART1)
  {
  /* USER CODE BEGIN LPUART1_MspInit 0 */

  /* USER CODE END LPUART1_MspInit 0 */

  /** Initializes the peripherals clocks
  */
    PeriphClkInit.PeriphClockSelection = RCC_PERIPHCLK_LPUART1;
    PeriphClkInit.Lpuart1ClockSelection = RCC_LPUART1CLKSOURCE_PCLK1;
    if (HAL_RCCEx_PeriphCLKConfig(&PeriphClkInit) != HAL_OK)
    {
      Error_Handler();
    }

    /* LPUART1 clock enable */
    __HAL_RCC_LPUART1_CLK_ENABLE();

    __HAL_RCC_GPIOA_CLK_ENABLE();
    /**LPUART1 GPIO Configuration
    PA2     ------> LPUART1_TX
    PA3     ------> LPUART1_RX
    */
    GPIO_InitStruct.Pin = GPIO_PIN_2|GPIO_PIN_3;
    GPIO_InitStruct.Mode = GPIO_MODE_AF_PP;
    GPIO_InitStruct.Pull = GPIO_NOPULL;
    GPIO_InitStruct.Speed = GPIO_SPEED_FREQ_LOW;
    GPIO_InitStruct.Alternate = GPIO_AF12_LPUART1;
    HAL_GPIO_Init(GPIOA, &GPIO_InitStruct);

    /* LPUART1 DMA Init */
    /* LPUART1_RX Init */
    hdma_lpuart1_rx.Instance = DMA1_Channel1;
    hdma_lpuart1_rx.Init.Request = DMA_REQUEST_LPUART1_RX;
    hdma_lpuart1_rx.Init.Direction = DMA_PERIPH_TO_MEMORY;
    hdma_lpuart1_rx.Init.PeriphInc = DMA_PINC_DISABLE;
    hdma_lpuart1_rx.Init.MemInc = DMA_MINC_ENABLE;
    hdma_lpuart1_rx.Init.PeriphDataAlignment = DMA_PDATAALIGN_BYTE;
    hdma_lpuart1_rx.Init.MemDataAlignment = DMA_MDATAALIGN_BYTE;
    hdma_lpuart1_rx.Init.Mode = DMA_NORMAL;
    hdma_lpuart1_rx.Init.Priority = DMA_PRIORITY_LOW;
    if (HAL_DMA_Init(&hdma_lpuart1_rx) != HAL_OK)
    {
      Error_Handler();
    }

    __HAL_LINKDMA(uartHandle,hdmarx,hdma_lpuart1_rx);

    /* LPUART1_TX Init */
    hdma_lpuart1_tx.Instance = DMA1_Channel2;
    hdma_lpuart1_tx.Init.Request = DMA_REQUEST_LPUART1_TX;
    hdma_lpuart1_tx.Init.Direction = DMA_MEMORY_TO_PERIPH;
    hdma_lpuart1_tx.Init.PeriphInc = DMA_PINC_DISABLE;
    hdma_lpuart1_tx.Init.MemInc = DMA_MINC_ENABLE;
    hdma_lpuart1_tx.Init.PeriphDataAlignment = DMA_PDATAALIGN_BYTE;
    hdma_lpuart1_tx.Init.MemDataAlignment = DMA_MDATAALIGN_BYTE;
    hdma_lpuart1_tx.Init.Mode = DMA_NORMAL;
    hdma_lpuart1_tx.Init.Priority = DMA_PRIORITY_LOW;
    if (HAL_DMA_Init(&hdma_lpuart1_tx) != HAL_OK)
    {
      Error_Handler();
    }

    __HAL_LINKDMA(uartHandle,hdmatx,hdma_lpuart1_tx);

    /* LPUART1 interrupt Init */
    HAL_NVIC_SetPriority(LPUART1_IRQn, 0, 0);
    HAL_NVIC_EnableIRQ(LPUART1_IRQn);
  /* USER CODE BEGIN LPUART1_MspInit 1 */

  /* USER CODE END LPUART1_MspInit 1 */
  }
}

void HAL_UART_MspDeInit(UART_HandleTypeDef* uartHandle)
{

  if(uartHandle->Instance==LPUART1)
  {
  /* USER CODE BEGIN LPUART1_MspDeInit 0 */

  /* USER CODE END LPUART1_MspDeInit 0 */
    /* Peripheral clock disable */
    __HAL_RCC_LPUART1_CLK_DISABLE();

    /**LPUART1 GPIO Configuration
    PA2     ------> LPUART1_TX
    PA3     ------> LPUART1_RX
    */
    HAL_GPIO_DeInit(GPIOA, GPIO_PIN_2|GPIO_PIN_3);

    /* LPUART1 DMA DeInit */
    HAL_DMA_DeInit(uartHandle->hdmarx);
    HAL_DMA_DeInit(uartHandle->hdmatx);

    /* LPUART1 interrupt Deinit */
    HAL_NVIC_DisableIRQ(LPUART1_IRQn);
  /* USER CODE BEGIN LPUART1_MspDeInit 1 */

  /* USER CODE END LPUART1_MspDeInit 1 */
  }
}

/* USER CODE BEGIN 1 */
static void LPUART1_StartRxDma(void)
{
  if (HAL_UART_Receive_DMA(&hlpuart1, &g_lpuart1RxByte, 1U) != HAL_OK)
  {
    Error_Handler();
  }
}

static void LPUART1_StartNextTxDma(void)
{
  if (g_lpuart1TxCount == 0U)
  {
    return;
  }

  if (HAL_UART_Transmit_DMA(&hlpuart1, g_lpuart1TxQueue[g_lpuart1TxTail], g_lpuart1TxSize[g_lpuart1TxTail]) != HAL_OK)
  {
    Error_Handler();
  }
}

static void LPUART1_QueueTxData(const uint8_t* data, uint16_t size)
{
  uint16_t i = 0U;
  uint16_t clipped_size = size;
  uint8_t was_empty = 0U;
  uint8_t queue_idx = 0U;

  if ((data == 0) || (size == 0U))
  {
    return;
  }

  if (clipped_size > SERIAL_DRIVER_TX_MAX_SIZE)
  {
    clipped_size = SERIAL_DRIVER_TX_MAX_SIZE;
  }

  __disable_irq();
  if (g_lpuart1TxCount >= SERIAL_DRIVER_TX_QUEUE_DEPTH)
  {
    __enable_irq();
    return;
  }

  queue_idx = g_lpuart1TxHead;
  for (i = 0U; i < clipped_size; i++)
  {
    g_lpuart1TxQueue[queue_idx][i] = data[i];
  }
  g_lpuart1TxSize[queue_idx] = clipped_size;

  was_empty = (g_lpuart1TxCount == 0U) ? 1U : 0U;
  g_lpuart1TxHead = (uint8_t)((g_lpuart1TxHead + 1U) % SERIAL_DRIVER_TX_QUEUE_DEPTH);
  g_lpuart1TxCount++;
  __enable_irq();

  if (was_empty != 0U)
  {
    LPUART1_StartNextTxDma();
  }
}

static uint8_t ICS2_CalcChecksum(uint8_t cmd, uint16_t len, const uint8_t *payload)
{
  uint32_t sum = 0U;
  uint16_t i = 0U;
  sum += cmd;
  sum += (uint8_t)(len & 0xFFU);
  sum += (uint8_t)((len >> 8) & 0xFFU);
  for (i = 0U; i < len; i++)
  {
    sum += payload[i];
  }
  return (uint8_t)(sum & 0xFFU);
}

static void ICS2_SendFrame(uint8_t cmd, const uint8_t *payload, uint16_t len)
{
  uint16_t i = 0U;
  uint16_t frame_size = (uint16_t)(6U + len);

  if (frame_size > SERIAL_DRIVER_TX_MAX_SIZE)
  {
    return;
  }

  g_ics2TxFrame[0] = ICS2_SYNC1;
  g_ics2TxFrame[1] = ICS2_SYNC2;
  g_ics2TxFrame[2] = cmd;
  g_ics2TxFrame[3] = (uint8_t)(len & 0xFFU);
  g_ics2TxFrame[4] = (uint8_t)((len >> 8) & 0xFFU);
  for (i = 0U; i < len; i++)
  {
    g_ics2TxFrame[5U + i] = payload[i];
  }
  g_ics2TxFrame[5U + len] = ICS2_CalcChecksum(cmd, len, payload);
  LPUART1_QueueTxData(g_ics2TxFrame, frame_size);
}

static void ICS2_SendAck(void)
{
  ICS2_SendFrame(ICS2_CMD_ACK, 0, 0U);
}

static void ICS2_SendNack(void)
{
  ICS2_SendFrame(ICS2_CMD_NACK, 0, 0U);
}

static void ICS2_SendNackWithReason(uint8_t reason)
{
  uint8_t payload[1];
  payload[0] = reason;
  ICS2_SendFrame(ICS2_CMD_NACK, payload, 1U);
}

static uint8_t ICS2_GetTypeSize(uint8_t type)
{
  switch (type)
  {
    case Ics2VarTypeU8:
    case Ics2VarTypeI8:
    case Ics2VarTypeBool:
    case Ics2VarTypeLogic:
      return 1U;
    case Ics2VarTypeU16:
    case Ics2VarTypeI16:
      return 2U;
    case Ics2VarTypeU32:
    case Ics2VarTypeI32:
    case Ics2VarTypeF32:
      return 4U;
    default:
      return 0U;
  }
}

static const Ics2VarEntry_t *ICS2_FindVariable(uint32_t address, uint8_t type)
{
  uint32_t i = 0U;
  for (i = 0U; i < (uint32_t)(sizeof(g_ics2VarTable) / sizeof(g_ics2VarTable[0])); i++)
  {
    if ((g_ics2VarTable[i].address == address) && (g_ics2VarTable[i].type == type))
    {
      return &g_ics2VarTable[i];
    }
  }
  return 0;
}

static uint8_t ICS2_ReadVariableRaw(const Ics2VarEntry_t *entry, uint8_t *out_raw)
{
  uint8_t size = ICS2_GetTypeSize(entry->type);
  const volatile uint8_t *src = (const volatile uint8_t *)(uintptr_t)entry->address;
  uint8_t i = 0U;

  if (size == 0U)
  {
    return 0U;
  }

  for (i = 0U; i < size; i++)
  {
    out_raw[i] = src[i];
  }
  return size;
}

static uint8_t ICS2_WriteVariableRaw(const Ics2VarEntry_t *entry, const uint8_t *raw, uint8_t raw_size)
{
  volatile uint8_t *dst = (volatile uint8_t *)(uintptr_t)entry->address;
  uint8_t i = 0U;
  uint8_t size = ICS2_GetTypeSize(entry->type);

  if ((entry->writable == 0U) || (size == 0U) || (size != raw_size))
  {
    return 0U;
  }

  for (i = 0U; i < size; i++)
  {
    dst[i] = raw[i];
  }
  return 1U;
}

static float ICS2_ReadVariableAsFloat(const Ics2VarEntry_t *entry)
{
  uint8_t raw[4] = {0};
  float value = 0.0f;
  int32_t signed_value = 0;
  uint32_t unsigned_value = 0U;

  (void)ICS2_ReadVariableRaw(entry, raw);
  switch (entry->type)
  {
    case Ics2VarTypeU8:
    case Ics2VarTypeBool:
    case Ics2VarTypeLogic:
      return (float)raw[0];
    case Ics2VarTypeI8:
      return (float)((int8_t)raw[0]);
    case Ics2VarTypeU16:
      unsigned_value = (uint32_t)raw[0] | ((uint32_t)raw[1] << 8);
      return (float)unsigned_value;
    case Ics2VarTypeI16:
      signed_value = (int32_t)((int16_t)((uint16_t)raw[0] | ((uint16_t)raw[1] << 8)));
      return (float)signed_value;
    case Ics2VarTypeU32:
      unsigned_value = (uint32_t)raw[0] | ((uint32_t)raw[1] << 8) | ((uint32_t)raw[2] << 16) | ((uint32_t)raw[3] << 24);
      return (float)unsigned_value;
    case Ics2VarTypeI32:
      signed_value = (int32_t)((uint32_t)raw[0] | ((uint32_t)raw[1] << 8) | ((uint32_t)raw[2] << 16) | ((uint32_t)raw[3] << 24));
      return (float)signed_value;
    case Ics2VarTypeF32:
      memcpy(&value, raw, sizeof(float));
      return value;
    default:
      return 0.0f;
  }
}

static void ICS2_HandleGetInfo(void)
{
  const char cpu_name[] = "STM32G431";
  const char lib_version[] = "ICS2-STM32-0.1";
  float clock_mhz = ((float)HAL_RCC_GetHCLKFreq()) / 1000000.0f;
  uint8_t payload[64];
  uint16_t idx = 0U;

  payload[idx++] = (uint8_t)(sizeof(cpu_name) - 1U);
  memcpy(&payload[idx], cpu_name, sizeof(cpu_name) - 1U);
  idx = (uint16_t)(idx + (sizeof(cpu_name) - 1U));
  payload[idx++] = (uint8_t)(sizeof(lib_version) - 1U);
  memcpy(&payload[idx], lib_version, sizeof(lib_version) - 1U);
  idx = (uint16_t)(idx + (sizeof(lib_version) - 1U));
  memcpy(&payload[idx], &clock_mhz, sizeof(float));
  idx = (uint16_t)(idx + sizeof(float));

  ICS2_SendFrame(ICS2_CMD_INFO_RESPONSE, payload, idx);
}

static void ICS2_HandleReadVariable(const uint8_t *payload, uint16_t len)
{
  uint8_t name_len = 0U;
  uint16_t idx = 0U;
  uint32_t address = 0U;
  uint8_t type = 0U;
  uint8_t raw[4] = {0};
  uint8_t raw_size = 0U;
  const Ics2VarEntry_t *entry = 0;
  uint8_t out[96];
  uint8_t out_name_len = 0U;

  if (len < 6U)
  {
    ICS2_SendNackWithReason(ICS2_NACK_INVALID_PAYLOAD);
    return;
  }

  name_len = payload[idx++];
  if (len < (uint16_t)(1U + name_len + 5U))
  {
    ICS2_SendNackWithReason(ICS2_NACK_INVALID_PAYLOAD);
    return;
  }
  idx = (uint16_t)(idx + name_len);
  address = (uint32_t)payload[idx]
          | ((uint32_t)payload[idx + 1U] << 8)
          | ((uint32_t)payload[idx + 2U] << 16)
          | ((uint32_t)payload[idx + 3U] << 24);
  idx = (uint16_t)(idx + 4U);
  type = payload[idx];

  entry = ICS2_FindVariable(address, type);
  if (entry == 0)
  {
    ICS2_SendNackWithReason(ICS2_NACK_BAD_VARIABLE);
    return;
  }

  raw_size = ICS2_ReadVariableRaw(entry, raw);
  out_name_len = (uint8_t)strlen(entry->name);
  out[0] = out_name_len;
  memcpy(&out[1], entry->name, out_name_len);
  out[1U + out_name_len] = type;
  memcpy(&out[2U + out_name_len], raw, raw_size);
  ICS2_SendFrame(ICS2_CMD_VARIABLE_DATA, out, (uint16_t)(2U + out_name_len + raw_size));
}

static void ICS2_HandleWriteVariable(const uint8_t *payload, uint16_t len)
{
  uint8_t name_len = 0U;
  uint16_t idx = 0U;
  uint32_t address = 0U;
  uint8_t type = 0U;
  uint8_t size = 0U;
  const Ics2VarEntry_t *entry = 0;

  if (len < 7U)
  {
    ICS2_SendNackWithReason(ICS2_NACK_INVALID_PAYLOAD);
    return;
  }

  name_len = payload[idx++];
  if (len < (uint16_t)(1U + name_len + 5U))
  {
    ICS2_SendNackWithReason(ICS2_NACK_INVALID_PAYLOAD);
    return;
  }
  idx = (uint16_t)(idx + name_len);
  address = (uint32_t)payload[idx]
          | ((uint32_t)payload[idx + 1U] << 8)
          | ((uint32_t)payload[idx + 2U] << 16)
          | ((uint32_t)payload[idx + 3U] << 24);
  idx = (uint16_t)(idx + 4U);
  type = payload[idx++];
  size = ICS2_GetTypeSize(type);

  if ((size == 0U) || ((uint16_t)(idx + size) > len))
  {
    ICS2_SendNackWithReason(ICS2_NACK_INVALID_PAYLOAD);
    return;
  }

  entry = ICS2_FindVariable(address, type);
  if (entry == 0)
  {
    ICS2_SendNackWithReason(ICS2_NACK_BAD_VARIABLE);
    return;
  }

  if (ICS2_WriteVariableRaw(entry, &payload[idx], size) == 0U)
  {
    ICS2_SendNackWithReason(ICS2_NACK_WRITE_DENIED);
    return;
  }

  ICS2_SendAck();
}

static void ICS2_HandleStartScope(const uint8_t *payload, uint16_t len)
{
  uint16_t idx = 0U;
  uint8_t i = 0U;
  uint8_t channel_count = 0U;
  float sample_period_s = 0.0f;
  int32_t record_length = 0;
  uint8_t slot = 0U;
  uint8_t type = 0U;
  uint32_t address = 0U;
  const Ics2VarEntry_t *entry = 0;

  if (len < 9U)
  {
    ICS2_SendNackWithReason(ICS2_NACK_BAD_SCOPE_CONFIG);
    return;
  }

  memcpy(&sample_period_s, &payload[idx], sizeof(float));
  idx = (uint16_t)(idx + sizeof(float));
  memcpy(&record_length, &payload[idx], sizeof(int32_t));
  idx = (uint16_t)(idx + sizeof(int32_t));
  channel_count = payload[idx++];

  if ((channel_count == 0U) || (channel_count > ICS2_MAX_SCOPE_CHANNELS))
  {
    ICS2_SendNackWithReason(ICS2_NACK_BAD_SCOPE_CONFIG);
    return;
  }
  if ((record_length <= 0) || (record_length > (int32_t)ICS2_MAX_SCOPE_RECORD_LENGTH))
  {
    ICS2_SendNackWithReason(ICS2_NACK_BAD_SCOPE_CONFIG);
    return;
  }
  if (len != (uint16_t)(9U + ((uint16_t)channel_count * 6U)))
  {
    ICS2_SendNackWithReason(ICS2_NACK_BAD_SCOPE_CONFIG);
    return;
  }

  for (i = 0U; i < channel_count; i++)
  {
    slot = payload[idx++];
    type = payload[idx++];
    address = (uint32_t)payload[idx]
            | ((uint32_t)payload[idx + 1U] << 8)
            | ((uint32_t)payload[idx + 2U] << 16)
            | ((uint32_t)payload[idx + 3U] << 24);
    idx = (uint16_t)(idx + 4U);

    if (slot >= ICS2_MAX_SCOPE_CHANNELS)
    {
      ICS2_SendNackWithReason(ICS2_NACK_BAD_SCOPE_CONFIG);
      return;
    }

    entry = ICS2_FindVariable(address, type);
    if (entry == 0)
    {
      ICS2_SendNackWithReason(ICS2_NACK_BAD_SCOPE_CONFIG);
      return;
    }

    g_ics2Scope.channels[i].slot = slot;
    g_ics2Scope.channels[i].type = type;
    g_ics2Scope.channels[i].entry = entry;
  }

  g_ics2Scope.sample_period_s = sample_period_s;
  g_ics2Scope.sample_period_ms = (uint32_t)(sample_period_s * 1000.0f);
  if (g_ics2Scope.sample_period_ms == 0U)
  {
    g_ics2Scope.sample_period_ms = 1U;
  }
  g_ics2Scope.record_length = record_length;
  g_ics2Scope.channel_count = channel_count;
  g_ics2Scope.sample_index = 0;
  g_ics2Scope.next_sample_tick = HAL_GetTick();
  g_ics2Scope.running = 1U;
  ICS2_SendAck();
}

static void ICS2_HandleStopScope(void)
{
  g_ics2Scope.running = 0U;
  g_ics2Scope.sample_index = 0;
  ICS2_SendAck();
}

static void ICS2_HandleSetTrigger(const uint8_t *payload, uint16_t len)
{
  if (len != 11U)
  {
    ICS2_SendNackWithReason(ICS2_NACK_BAD_TRIGGER_CONFIG);
    return;
  }

  memcpy(&g_ics2Trigger.position, &payload[0], sizeof(float));
  memcpy(&g_ics2Trigger.level, &payload[4], sizeof(float));
  g_ics2Trigger.source = payload[8];
  g_ics2Trigger.mode = payload[9];
  g_ics2Trigger.edge = payload[10];
  ICS2_SendAck();
}

static void ICS2_HandleFrame(uint8_t cmd, const uint8_t *payload, uint16_t len)
{
  switch (cmd)
  {
    case ICS2_CMD_PING:
      ICS2_SendAck();
      break;
    case ICS2_CMD_GET_INFO:
      ICS2_HandleGetInfo();
      break;
    case ICS2_CMD_READ_VARIABLE:
      ICS2_HandleReadVariable(payload, len);
      break;
    case ICS2_CMD_WRITE_VARIABLE:
      ICS2_HandleWriteVariable(payload, len);
      break;
    case ICS2_CMD_START_SCOPE:
      ICS2_HandleStartScope(payload, len);
      break;
    case ICS2_CMD_STOP_SCOPE:
      ICS2_HandleStopScope();
      break;
    case ICS2_CMD_SET_TRIGGER:
      ICS2_HandleSetTrigger(payload, len);
      break;
    default:
      ICS2_SendNackWithReason(ICS2_NACK_UNSUPPORTED_CMD);
      break;
  }
}

static void ICS2_OnByteReceived(uint8_t byte)
{
  switch (g_ics2RxState)
  {
    case Ics2StateWaitSync1:
      if (byte == ICS2_SYNC1)
      {
        g_ics2RxChecksum = 0U;
        g_ics2RxState = Ics2StateWaitSync2;
      }
      break;

    case Ics2StateWaitSync2:
      if (byte == ICS2_SYNC2)
      {
        g_ics2RxState = Ics2StateCmd;
      }
      else
      {
        g_ics2RxState = Ics2StateWaitSync1;
      }
      break;

    case Ics2StateCmd:
      g_ics2RxCmd = byte;
      g_ics2RxChecksum = (uint8_t)(g_ics2RxChecksum + byte);
      g_ics2RxState = Ics2StateLenL;
      break;

    case Ics2StateLenL:
      g_ics2RxLen = byte;
      g_ics2RxChecksum = (uint8_t)(g_ics2RxChecksum + byte);
      g_ics2RxState = Ics2StateLenH;
      break;

    case Ics2StateLenH:
      g_ics2RxLen |= (uint16_t)((uint16_t)byte << 8);
      g_ics2RxChecksum = (uint8_t)(g_ics2RxChecksum + byte);
      g_ics2RxIndex = 0U;
      if (g_ics2RxLen == 0U)
      {
        g_ics2RxState = Ics2StateChecksum;
      }
      else if (g_ics2RxLen > ICS2_MAX_PAYLOAD_SIZE)
      {
        g_ics2RxState = Ics2StateWaitSync1;
      }
      else
      {
        g_ics2RxState = Ics2StatePayload;
      }
      break;

    case Ics2StatePayload:
      g_ics2RxPayload[g_ics2RxIndex++] = byte;
      g_ics2RxChecksum = (uint8_t)(g_ics2RxChecksum + byte);
      if (g_ics2RxIndex >= g_ics2RxLen)
      {
        g_ics2RxState = Ics2StateChecksum;
      }
      break;

    case Ics2StateChecksum:
      if (g_ics2RxChecksum == byte)
      {
        ICS2_HandleFrame(g_ics2RxCmd, g_ics2RxPayload, g_ics2RxLen);
      }
      else
      {
        ICS2_SendNackWithReason(ICS2_NACK_INVALID_PAYLOAD);
      }
      g_ics2RxState = Ics2StateWaitSync1;
      break;

    default:
      g_ics2RxState = Ics2StateWaitSync1;
      break;
  }
}

static void ICS2_StreamWaveform(void)
{
  static uint8_t payload[SERIAL_DRIVER_TX_MAX_SIZE];
  uint8_t ch = 0U;
  uint16_t idx = 0U;
  uint16_t count = (uint16_t)g_ics2Scope.record_length;
  uint16_t i = 0U;

  for (ch = 0U; ch < g_ics2Scope.channel_count; ch++)
  {
    idx = 0U;
    payload[idx++] = g_ics2Scope.channels[ch].slot;
    memcpy(&payload[idx], &g_ics2Scope.sample_period_s, sizeof(float));
    idx = (uint16_t)(idx + sizeof(float));
    payload[idx++] = (uint8_t)(count & 0xFFU);
    payload[idx++] = (uint8_t)((count >> 8) & 0xFFU);
    for (i = 0U; i < count; i++)
    {
      memcpy(&payload[idx], &g_ics2Scope.sample_data[ch][i], sizeof(float));
      idx = (uint16_t)(idx + sizeof(float));
    }
    ICS2_SendFrame(ICS2_CMD_WAVEFORM_DATA, payload, idx);
  }
}

void ICS2_ProtocolPoll(void)
{
  uint8_t ch = 0U;
  uint32_t now_tick = HAL_GetTick();

  if (g_ics2Scope.running == 0U)
  {
    return;
  }

  if (now_tick < g_ics2Scope.next_sample_tick)
  {
    return;
  }

  g_ics2Scope.next_sample_tick = now_tick + g_ics2Scope.sample_period_ms;
  for (ch = 0U; ch < g_ics2Scope.channel_count; ch++)
  {
    g_ics2Scope.sample_data[ch][g_ics2Scope.sample_index] = ICS2_ReadVariableAsFloat(g_ics2Scope.channels[ch].entry);
  }
  g_ics2Scope.sample_index++;

  if (g_ics2Scope.sample_index >= g_ics2Scope.record_length)
  {
    g_ics2Scope.sample_index = 0;
    ICS2_StreamWaveform();
  }
}

void HAL_UART_RxCpltCallback(UART_HandleTypeDef* huart)
{
  if (huart->Instance == LPUART1)
  {
#if SERIAL_DRIVER_UART_SELF_TEST
    LPUART1_QueueTxData(&g_lpuart1RxByte, 1U);
#else
#if USE_ICS2_PROTOCOL
    ICS2_OnByteReceived(g_lpuart1RxByte);
#else
    serialDriverReceiveByte(g_lpuart1RxByte);
#endif
#endif

    if (HAL_UART_Receive_DMA(huart, &g_lpuart1RxByte, 1U) != HAL_OK)
    {
      Error_Handler();
    }
  }
}

void serialDriverSendData(uint8_t* buf, uint16_t size)
{
#if USE_ICS2_PROTOCOL
  (void)buf;
  (void)size;
#else
  LPUART1_QueueTxData(buf, size);
#endif
}

void HAL_UART_TxCpltCallback(UART_HandleTypeDef* huart)
{
  if (huart->Instance == LPUART1)
  {
    __disable_irq();
    if (g_lpuart1TxCount > 0U)
    {
      g_lpuart1TxTail = (uint8_t)((g_lpuart1TxTail + 1U) % SERIAL_DRIVER_TX_QUEUE_DEPTH);
      g_lpuart1TxCount--;
    }
    __enable_irq();

    LPUART1_StartNextTxDma();
  }
}

void HAL_UART_ErrorCallback(UART_HandleTypeDef* huart)
{
  if (huart->Instance == LPUART1)
  {
    (void)HAL_UART_DMAStop(huart);
    __disable_irq();
    g_lpuart1TxHead = 0U;
    g_lpuart1TxTail = 0U;
    g_lpuart1TxCount = 0U;
    __enable_irq();
    LPUART1_StartRxDma();
  }
}

/* USER CODE END 1 */
