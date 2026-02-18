/* USER CODE BEGIN Header */
/**
  ******************************************************************************
  * @file           : main.c
  * @brief          : Main program body
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
#include "main.h"
#include "dma.h"
#include "usart.h"
#include "gpio.h"

/* Private includes ----------------------------------------------------------*/
/* USER CODE BEGIN Includes */
#include "motor_sim_vars.h"

/* USER CODE END Includes */

/* Private typedef -----------------------------------------------------------*/
/* USER CODE BEGIN PTD */
typedef struct
{
  float amplitude;
  float a;
  float b;
  float c;
} MCUViewerSvm_t;

typedef struct
{
  float a;
  float b;
  float c;
  uint32_t frac_u32;
  float step;
  MCUViewerSvm_t svm;
  float triangle;
  float triangleFrequency;
} MCUViewerTest_t;

/* USER CODE END PTD */

/* Private define ------------------------------------------------------------*/
/* USER CODE BEGIN PD */

/* USER CODE END PD */

/* Private macro -------------------------------------------------------------*/
/* USER CODE BEGIN PM */

/* USER CODE END PM */

/* Private variables ---------------------------------------------------------*/

/* USER CODE BEGIN PV */
volatile MCUViewerTest_t test;
volatile uint8_t com_u1_system_mode = 0U;
volatile uint8_t g_u1_system_mode = 0U;
volatile float com_f4_ref_speed_rpm = 0.0f;
volatile float com_f4_speed_rate_limit_rpm = 600.0f;
volatile sim_sensorless_vector_t g_st_sensorless_vector = {0};

/* USER CODE END PV */

/* Private function prototypes -----------------------------------------------*/
void SystemClock_Config(void);
/* USER CODE BEGIN PFP */
static float WrapPu(float pu);
static float FastSinPu(float pu);
static float TriangleFromPu(float pu);
static void MCUViewer_UpdateSignals(void);
static void MotorSim_UpdateState(void);

/* USER CODE END PFP */

/* Private user code ---------------------------------------------------------*/
/* USER CODE BEGIN 0 */
static float WrapPu(float pu)
{
  while (pu >= 1.0f)
  {
    pu -= 1.0f;
  }
  while (pu < 0.0f)
  {
    pu += 1.0f;
  }

  return pu;
}

static float FastSinPu(float pu)
{
  float x = WrapPu(pu);
  float abs_x = 0.0f;
  float y = 0.0f;
  float abs_y = 0.0f;

  if (x >= 0.5f)
  {
    x -= 1.0f;
  }

  x = x * 6.283185307f;
  abs_x = (x < 0.0f) ? -x : x;

  /* Fast sine approximation to keep runtime overhead low. */
  y = x * (1.273239544f - (0.405284735f * abs_x));
  abs_y = (y < 0.0f) ? -y : y;
  y = y + (0.225f * ((y * abs_y) - y));

  return y;
}

static float TriangleFromPu(float pu)
{
  float x = WrapPu(pu);

  if (x < 0.25f)
  {
    return 4.0f * x;
  }

  if (x < 0.75f)
  {
    return 2.0f - (4.0f * x);
  }

  return -4.0f + (4.0f * x);
}

static void MCUViewer_UpdateSignals(void)
{
  static uint32_t last_tick = 0U;
  static float phase_svm = 0.0f;
  static float phase_triangle = 0.0f;
  uint32_t now_tick = HAL_GetTick();
  uint32_t dt_ms = 0U;
  float step = 0.0f;
  float amplitude = 0.0f;
  float triangle_frequency = 0.0f;

  if (now_tick == last_tick)
  {
    return;
  }

  dt_ms = now_tick - last_tick;
  last_tick = now_tick;

  step = test.step;
  if (step < 0.0f)
  {
    step = 0.0f;
    test.step = step;
  }
  if (step > 0.25f)
  {
    step = 0.25f;
    test.step = step;
  }
  phase_svm = WrapPu(phase_svm + (step * (float)dt_ms));

  triangle_frequency = test.triangleFrequency;
  if (triangle_frequency < 0.0f)
  {
    triangle_frequency = 0.0f;
    test.triangleFrequency = triangle_frequency;
  }
  if (triangle_frequency > 200.0f)
  {
    triangle_frequency = 200.0f;
    test.triangleFrequency = triangle_frequency;
  }
  phase_triangle = WrapPu(phase_triangle + (triangle_frequency * 0.001f * (float)dt_ms));

  test.a = FastSinPu(phase_svm);
  test.b = FastSinPu(phase_svm + (1.0f / 3.0f));
  test.c = FastSinPu(phase_svm + (2.0f / 3.0f));

  amplitude = test.svm.amplitude;
  if (amplitude < 0.0f)
  {
    amplitude = 0.0f;
    test.svm.amplitude = amplitude;
  }
  if (amplitude > 1.0f)
  {
    amplitude = 1.0f;
    test.svm.amplitude = amplitude;
  }
  test.svm.a = amplitude * test.a;
  test.svm.b = amplitude * test.b;
  test.svm.c = amplitude * test.c;

  test.triangle = TriangleFromPu(phase_triangle);
  test.frac_u32 = (uint32_t)(phase_svm * 4294967295.0f);
}

static float ClampF32(float value, float min_value, float max_value)
{
  if (value < min_value)
  {
    return min_value;
  }
  if (value > max_value)
  {
    return max_value;
  }
  return value;
}

static void MotorSim_UpdateState(void)
{
  static uint32_t last_tick = 0U;
  static float speed_rpm = 0.0f;
  static float phase = 0.0f;
  uint32_t now_tick = HAL_GetTick();
  uint32_t dt_ms = 0U;
  float dt_s = 0.0f;
  float target_rpm = 0.0f;
  float slew_limit = 0.0f;
  float delta = 0.0f;
  float amp = 0.0f;

  if (now_tick == last_tick)
  {
    return;
  }

  dt_ms = now_tick - last_tick;
  last_tick = now_tick;
  dt_s = 0.001f * (float)dt_ms;

  if (com_u1_system_mode == 1U)
  {
    g_u1_system_mode = 1U;
    target_rpm = ClampF32(com_f4_ref_speed_rpm, -5000.0f, 5000.0f);
    slew_limit = ClampF32(com_f4_speed_rate_limit_rpm, 10.0f, 10000.0f) * dt_s;
    delta = target_rpm - speed_rpm;
    delta = ClampF32(delta, -slew_limit, slew_limit);
    speed_rpm += delta;
  }
  else
  {
    g_u1_system_mode = 0U;
    target_rpm = 0.0f;
    speed_rpm *= 0.96f;
    if ((speed_rpm < 0.5f) && (speed_rpm > -0.5f))
    {
      speed_rpm = 0.0f;
    }
  }

  phase = WrapPu(phase + ((speed_rpm / 60.0f) * dt_s));
  amp = ClampF32((speed_rpm >= 0.0f ? speed_rpm : -speed_rpm) / 3000.0f, 0.0f, 1.0f);

  g_st_sensorless_vector.f4_vdc_ad = 24.0f + (1.0f * FastSinPu(phase * 0.05f));
  g_st_sensorless_vector.f4_iu_ad = amp * FastSinPu(phase);
  g_st_sensorless_vector.f4_iv_ad = amp * FastSinPu(phase + (1.0f / 3.0f));
  g_st_sensorless_vector.f4_iw_ad = amp * FastSinPu(phase + (2.0f / 3.0f));

  g_st_sensorless_vector.st_speed_output.f4_speed_rad_lpf = speed_rpm * 0.10471976f;
  g_st_sensorless_vector.st_speed_output.f4_ref_speed_rad_ctrl = target_rpm * 0.10471976f;
  g_st_sensorless_vector.st_speed_output.f4_id_ref = 0.15f * amp;
  g_st_sensorless_vector.st_speed_output.f4_iq_ref = 0.85f * amp;

  g_st_sensorless_vector.st_current_output.u1_flag_offset_calc = 1U;
  g_st_sensorless_vector.st_current_output.u1_flag_charge_bootstrap = 1U;
  g_st_sensorless_vector.st_current_output.f4_ref_id_ctrl = g_st_sensorless_vector.st_speed_output.f4_id_ref;
  g_st_sensorless_vector.st_current_output.f4_speed_rad = speed_rpm * 0.10471976f;
  g_st_sensorless_vector.st_current_output.f4_ed = amp * FastSinPu(phase + 0.25f);
  g_st_sensorless_vector.st_current_output.f4_eq = amp * FastSinPu(phase + 0.50f);
  g_st_sensorless_vector.st_current_output.f4_phase_err_rad = 0.02f * FastSinPu(phase * 0.2f);

  g_st_sensorless_vector.st_stm.u1_status = (g_u1_system_mode == 1U) ? 2U : 1U;
}

/* USER CODE END 0 */

/**
  * @brief  The application entry point.
  * @retval int
  */
int main(void)
{

  /* USER CODE BEGIN 1 */

  /* USER CODE END 1 */

  /* MCU Configuration--------------------------------------------------------*/

  /* Reset of all peripherals, Initializes the Flash interface and the Systick. */
  HAL_Init();

  /* USER CODE BEGIN Init */

  /* USER CODE END Init */

  /* Configure the system clock */
  SystemClock_Config();

  /* USER CODE BEGIN SysInit */

  /* USER CODE END SysInit */

  /* Initialize all configured peripherals */
  MX_GPIO_Init();
  MX_DMA_Init();
  MX_LPUART1_UART_Init();
  /* USER CODE BEGIN 2 */
  uint32_t led_tick = HAL_GetTick();
  test.step = 0.02f;
  test.svm.amplitude = 0.5f;
  test.triangleFrequency = 5.0f;
  com_u1_system_mode = 0U;
  com_f4_ref_speed_rpm = 600.0f;
  com_f4_speed_rate_limit_rpm = 1200.0f;

  /* USER CODE END 2 */

  /* Infinite loop */
  /* USER CODE BEGIN WHILE */
  while (1)
  {
    /* USER CODE END WHILE */

    /* USER CODE BEGIN 3 */
    MCUViewer_UpdateSignals();
    MotorSim_UpdateState();
    ICS2_ProtocolPoll();
    if ((HAL_GetTick() - led_tick) >= 200U)
    {
      led_tick = HAL_GetTick();
      HAL_GPIO_TogglePin(GPIOA, GPIO_PIN_5);
    }
	}
  /* USER CODE END 3 */
}

/**
  * @brief System Clock Configuration
  * @retval None
  */
void SystemClock_Config(void)
{
  RCC_OscInitTypeDef RCC_OscInitStruct = {0};
  RCC_ClkInitTypeDef RCC_ClkInitStruct = {0};

  /** Configure the main internal regulator output voltage
  */
  HAL_PWREx_ControlVoltageScaling(PWR_REGULATOR_VOLTAGE_SCALE1_BOOST);

  /** Initializes the RCC Oscillators according to the specified parameters
  * in the RCC_OscInitTypeDef structure.
  */
  RCC_OscInitStruct.OscillatorType = RCC_OSCILLATORTYPE_HSI;
  RCC_OscInitStruct.HSIState = RCC_HSI_ON;
  RCC_OscInitStruct.HSICalibrationValue = RCC_HSICALIBRATION_DEFAULT;
  RCC_OscInitStruct.PLL.PLLState = RCC_PLL_ON;
  RCC_OscInitStruct.PLL.PLLSource = RCC_PLLSOURCE_HSI;
  RCC_OscInitStruct.PLL.PLLM = RCC_PLLM_DIV1;
  RCC_OscInitStruct.PLL.PLLN = 21;
  RCC_OscInitStruct.PLL.PLLP = RCC_PLLP_DIV2;
  RCC_OscInitStruct.PLL.PLLQ = RCC_PLLQ_DIV2;
  RCC_OscInitStruct.PLL.PLLR = RCC_PLLR_DIV2;
  if (HAL_RCC_OscConfig(&RCC_OscInitStruct) != HAL_OK)
  {
    Error_Handler();
  }

  /** Initializes the CPU, AHB and APB buses clocks
  */
  RCC_ClkInitStruct.ClockType = RCC_CLOCKTYPE_HCLK|RCC_CLOCKTYPE_SYSCLK
                              |RCC_CLOCKTYPE_PCLK1|RCC_CLOCKTYPE_PCLK2;
  RCC_ClkInitStruct.SYSCLKSource = RCC_SYSCLKSOURCE_PLLCLK;
  RCC_ClkInitStruct.AHBCLKDivider = RCC_SYSCLK_DIV1;
  RCC_ClkInitStruct.APB1CLKDivider = RCC_HCLK_DIV1;
  RCC_ClkInitStruct.APB2CLKDivider = RCC_HCLK_DIV1;

  if (HAL_RCC_ClockConfig(&RCC_ClkInitStruct, FLASH_LATENCY_4) != HAL_OK)
  {
    Error_Handler();
  }
}

/* USER CODE BEGIN 4 */

/* USER CODE END 4 */

/**
  * @brief  This function is executed in case of error occurrence.
  * @retval None
  */
void Error_Handler(void)
{
  /* USER CODE BEGIN Error_Handler_Debug */
  /* User can add his own implementation to report the HAL error return state */
  __disable_irq();
  while (1)
  {
  }
  /* USER CODE END Error_Handler_Debug */
}
#ifdef USE_FULL_ASSERT
/**
  * @brief  Reports the name of the source file and the source line number
  *         where the assert_param error has occurred.
  * @param  file: pointer to the source file name
  * @param  line: assert_param error line source number
  * @retval None
  */
void assert_failed(uint8_t *file, uint32_t line)
{
  /* USER CODE BEGIN 6 */
  /* User can add his own implementation to report the file name and line number,
     ex: printf("Wrong parameters value: file %s on line %d\r\n", file, line) */
  /* USER CODE END 6 */
}
#endif /* USE_FULL_ASSERT */
