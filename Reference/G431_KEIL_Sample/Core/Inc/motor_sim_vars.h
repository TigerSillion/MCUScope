#ifndef MOTOR_SIM_VARS_H
#define MOTOR_SIM_VARS_H

#include <stdint.h>

typedef struct
{
  float f4_speed_rad_lpf;
  float f4_ref_speed_rad_ctrl;
  float f4_id_ref;
  float f4_iq_ref;
} sim_speed_output_t;

typedef struct
{
  uint8_t u1_flag_offset_calc;
  uint8_t u1_flag_charge_bootstrap;
  float f4_ref_id_ctrl;
  float f4_speed_rad;
  float f4_ed;
  float f4_eq;
  float f4_phase_err_rad;
} sim_current_output_t;

typedef struct
{
  uint8_t u1_status;
} sim_state_machine_t;

typedef struct
{
  float f4_vdc_ad;
  float f4_iu_ad;
  float f4_iv_ad;
  float f4_iw_ad;
  sim_speed_output_t st_speed_output;
  sim_current_output_t st_current_output;
  sim_state_machine_t st_stm;
} sim_sensorless_vector_t;

extern volatile uint8_t com_u1_system_mode;
extern volatile uint8_t g_u1_system_mode;
extern volatile float com_f4_ref_speed_rpm;
extern volatile float com_f4_speed_rate_limit_rpm;
extern volatile sim_sensorless_vector_t g_st_sensorless_vector;

#endif
