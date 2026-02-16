/***********************************************************************************************************************
* File Name   : r_motor_current_trq_vib_comp.c
* Description : Open C implementation of torque vibration compensation.
***********************************************************************************************************************/

#include <math.h>
#include <string.h>

#include "r_motor_current_trq_vib_comp.h"
#include "r_motor_filter.h"

#define TRQVIB_MIN_PARAM              (1.0e-6f)

static float motor_trqvib_wrap_0_2pi(float f4_angle)
{
    while (f4_angle >= MTR_TWOPI)
    {
        f4_angle -= MTR_TWOPI;
    }

    while (f4_angle < 0.0f)
    {
        f4_angle += MTR_TWOPI;
    }

    return f4_angle;
}

static float motor_trqvib_wrap_pm_pi(float f4_angle)
{
    while (f4_angle > 3.1415926535f)
    {
        f4_angle -= MTR_TWOPI;
    }

    while (f4_angle < -3.1415926535f)
    {
        f4_angle += MTR_TWOPI;
    }

    return f4_angle;
}

static uint16_t motor_trqvib_table_index(float f4_angle, uint16_t u2_table_size)
{
    float f4_pos = motor_trqvib_wrap_0_2pi(f4_angle) * ((float)u2_table_size / MTR_TWOPI);
    uint16_t u2_idx = (uint16_t)f4_pos;

    if (u2_idx >= u2_table_size)
    {
        u2_idx = (uint16_t)(u2_table_size - 1U);
    }

    return u2_idx;
}

static void motor_trqvib_target_zero(st_trqvib_comp_target_t *p_st_target)
{
    if (p_st_target == 0)
    {
        return;
    }

    p_st_target->u1_target_learning_done = MTR_FLG_CLR;
    p_st_target->u1_target_output_enable = MTR_FLG_CLR;
    p_st_target->s2_last_index = -1;
    p_st_target->s2_last_index_comp = -1;
    p_st_target->f4_angle_rad_machine = 0.0f;
    p_st_target->f4_angle_electric_last = 0.0f;
    p_st_target->f4_last_spd_comp = 0.0f;
    p_st_target->f4_last_index_comp = 0.0f;
    memset(p_st_target->f4_repetitive_table, 0, sizeof(p_st_target->f4_repetitive_table));
    memset(p_st_target->f4_fitting_coeff, 0, sizeof(p_st_target->f4_fitting_coeff));
}

void motor_current_trq_vibration_compensation_init(st_trqvib_comp_t *p_st_trqvib_comp,
                                                   st_trqvib_comp_cfg_t *p_st_trqvib_comp_cfg)
{
    if ((p_st_trqvib_comp == 0) || (p_st_trqvib_comp_cfg == 0))
    {
        return;
    }

    memset(p_st_trqvib_comp, 0, sizeof(st_trqvib_comp_t));
    motor_current_trq_vibration_compensation_parameter_set(p_st_trqvib_comp, p_st_trqvib_comp_cfg);
    motor_current_trq_vibration_compensation_reset(p_st_trqvib_comp);
}

void motor_current_trq_vibration_compensation_reset (st_trqvib_comp_t *p_st_trqvib_comp)
{
    if (p_st_trqvib_comp == 0)
    {
        return;
    }

    p_st_trqvib_comp->u1_flag_trqvib_comp_learning = MTR_FLG_CLR;
    p_st_trqvib_comp->u2_trqcomp_state = TRQCOMP_STATE_IDLE;
    p_st_trqvib_comp->u2_trqcomp_action = TRQCOMP_ACTION_RESET;
    p_st_trqvib_comp->f4_last_spd_ref = 0.0f;
    p_st_trqvib_comp->f4_last_spd_det = 0.0f;
    p_st_trqvib_comp->f4_tf_output = 0.0f;

    motor_current_trq_vibration_compensation_trackingfilter_reset(p_st_trqvib_comp);
    motor_current_trq_vibration_compensation_tvc_max_min_reset(p_st_trqvib_comp);
    motor_current_trq_vibration_compensation_tvc_max_min_1cycle_reset(p_st_trqvib_comp);
    motor_current_trq_vibration_compensation_tf_lpf_dc_reset(p_st_trqvib_comp);

    motor_trqvib_target_zero(&p_st_trqvib_comp->st_trqvib_comp_1f);
    motor_trqvib_target_zero(&p_st_trqvib_comp->st_trqvib_comp_2f);
}

void motor_current_trq_vibration_compensation_parameter_set(st_trqvib_comp_t *p_st_trqvib_comp,
                                                            const st_trqvib_comp_cfg_t *p_st_trqvib_comp_cfg)
{
    if ((p_st_trqvib_comp == 0) || (p_st_trqvib_comp_cfg == 0))
    {
        return;
    }

    p_st_trqvib_comp->u1_target_2f = p_st_trqvib_comp_cfg->u1_target_2f;
    p_st_trqvib_comp->f4_input_weight2 = p_st_trqvib_comp_cfg->f4_input_weight2;
    p_st_trqvib_comp->f4_input_weight1 = p_st_trqvib_comp_cfg->f4_input_weight1;
    p_st_trqvib_comp->f4_input_weight0 = p_st_trqvib_comp_cfg->f4_input_weight0;
    p_st_trqvib_comp->f4_tf_lpf_time = p_st_trqvib_comp_cfg->f4_tf_lpf_time;

    p_st_trqvib_comp->st_trqvib_comp_1f.u2_motor_pp = p_st_trqvib_comp_cfg->u2_motor_pp;
    p_st_trqvib_comp->st_trqvib_comp_1f.f4_output_gain = p_st_trqvib_comp_cfg->f4_output_gain_1f;
    p_st_trqvib_comp->st_trqvib_comp_1f.f4_harmonic_order = 1.0f;
    p_st_trqvib_comp->st_trqvib_comp_1f.f4_suppression_th = p_st_trqvib_comp_cfg->f4_suppression_th_1f;
    p_st_trqvib_comp->st_trqvib_comp_1f.f4_abnormal_output_th = p_st_trqvib_comp_cfg->f4_abnormal_output_th_1f;
    p_st_trqvib_comp->st_trqvib_comp_1f.f4_k2_timelead = p_st_trqvib_comp_cfg->f4_k2_timelead_1f;
    p_st_trqvib_comp->st_trqvib_comp_1f.u2_k2_timelead = p_st_trqvib_comp_cfg->u2_k2_timelead_1f;

    p_st_trqvib_comp->st_trqvib_comp_2f.u2_motor_pp = p_st_trqvib_comp_cfg->u2_motor_pp;
    p_st_trqvib_comp->st_trqvib_comp_2f.f4_output_gain = p_st_trqvib_comp_cfg->f4_output_gain_2f;
    p_st_trqvib_comp->st_trqvib_comp_2f.f4_harmonic_order = 2.0f;
    p_st_trqvib_comp->st_trqvib_comp_2f.f4_suppression_th = p_st_trqvib_comp_cfg->f4_suppression_th_2f;
    p_st_trqvib_comp->st_trqvib_comp_2f.f4_abnormal_output_th = p_st_trqvib_comp_cfg->f4_abnormal_output_th_2f;
    p_st_trqvib_comp->st_trqvib_comp_2f.f4_k2_timelead = p_st_trqvib_comp_cfg->f4_k2_timelead_2f;
    p_st_trqvib_comp->st_trqvib_comp_2f.u2_k2_timelead = p_st_trqvib_comp_cfg->u2_k2_timelead_2f;
}

void motor_current_trq_vibration_compensation_preproc(st_trqvib_comp_t *p_st_trqvib_comp,
                                                     const float f4_angle_electric,
                                                     const float f4_speed_ref_rad,
                                                     const float f4_speed_det_rad)
{
    float f4_machine_angle_pre;
    float f4_machine_angle_now;
    float f4_tf_out;
    float f4_tf_in;

    if (p_st_trqvib_comp == 0)
    {
        return;
    }

    f4_machine_angle_pre = p_st_trqvib_comp->st_trqvib_comp_1f.f4_angle_rad_machine;
    f4_machine_angle_now = motor_current_trq_vibration_compensation_get_m_angle(&p_st_trqvib_comp->st_trqvib_comp_1f,
                                                                                f4_angle_electric);

    p_st_trqvib_comp->st_trqvib_comp_1f.f4_angle_rad_machine = f4_machine_angle_now;
    p_st_trqvib_comp->st_trqvib_comp_2f.f4_angle_rad_machine = f4_machine_angle_now;
    p_st_trqvib_comp->st_trqvib_comp_1f.f4_angle_electric_last = f4_angle_electric;
    p_st_trqvib_comp->st_trqvib_comp_2f.f4_angle_electric_last = f4_angle_electric;

    f4_tf_in = f4_speed_ref_rad - f4_speed_det_rad;
    motor_current_trq_vibration_compensation_trackingfilter(p_st_trqvib_comp,
                                                            f4_tf_in,
                                                            &f4_tf_out,
                                                            f4_machine_angle_now);
    p_st_trqvib_comp->f4_tf_output = f4_tf_out;
    p_st_trqvib_comp->f4_last_spd_ref = f4_speed_ref_rad;
    p_st_trqvib_comp->f4_last_spd_det = f4_speed_det_rad;

    motor_current_trq_vibration_compensation_learning_management(p_st_trqvib_comp,
                                                                 f4_machine_angle_pre,
                                                                 f4_machine_angle_now);
    motor_current_trq_vibration_compensation_abnormal_prevention(p_st_trqvib_comp,
                                                                 f4_machine_angle_pre,
                                                                 f4_machine_angle_now);
}

void motor_current_trq_vibration_compensation_main_LUT(st_trqvib_comp_t *p_st_trqvib_comp,
                                                       st_trqvib_comp_target_t *p_st_trqvib_comp_target,
                                                       const float f4_speed_ref_rad,
                                                       const float f4_speed_det_rad)
{
    uint16_t u2_idx;
    uint16_t u2_idx_comp;
    float f4_input;

    if ((p_st_trqvib_comp == 0) || (p_st_trqvib_comp_target == 0))
    {
        return;
    }

    f4_input = f4_speed_ref_rad - f4_speed_det_rad;
    u2_idx = motor_trqvib_table_index(p_st_trqvib_comp_target->f4_angle_rad_machine
                                      * p_st_trqvib_comp_target->f4_harmonic_order,
                                      TRQVIB_COMP_ARY_SIZE);
    u2_idx_comp = (uint16_t)((u2_idx + p_st_trqvib_comp_target->u2_k2_timelead) % TRQVIB_COMP_ARY_SIZE);

    if (p_st_trqvib_comp->u1_flag_trqvib_comp_learning == MTR_FLG_SET)
    {
        float f4_prev = p_st_trqvib_comp_target->f4_repetitive_table[u2_idx];
        float f4_new = (p_st_trqvib_comp->f4_input_weight2 * f4_prev)
                     + (p_st_trqvib_comp->f4_input_weight1 * p_st_trqvib_comp_target->f4_last_spd_comp)
                     + (p_st_trqvib_comp->f4_input_weight0 * f4_input)
                     + (p_st_trqvib_comp_target->f4_output_gain * p_st_trqvib_comp->f4_tf_output);
        p_st_trqvib_comp_target->f4_repetitive_table[u2_idx] = f4_new;
    }

    p_st_trqvib_comp_target->s2_last_index = (int16_t)u2_idx;
    p_st_trqvib_comp_target->s2_last_index_comp = (int16_t)u2_idx_comp;

    if ((p_st_trqvib_comp_target->u1_target_output_enable == MTR_FLG_SET)
        && (p_st_trqvib_comp->u2_trqcomp_state != TRQCOMP_STATE_IDLE))
    {
        p_st_trqvib_comp_target->f4_last_spd_comp = p_st_trqvib_comp_target->f4_repetitive_table[u2_idx_comp];
    }
    else
    {
        p_st_trqvib_comp_target->f4_last_spd_comp = 0.0f;
    }
}

void motor_current_trq_vibration_compensation_trackingfilter(st_trqvib_comp_t *p_st_trqvib_comp,
                                                             const float f4_input_val,
                                                             float * p_f4_output_val,
                                                             const float f4_angle)
{
    float f4_gain;
    float f4_cos;
    float f4_sin;
    float f4_x;
    float f4_y;

    if ((p_st_trqvib_comp == 0) || (p_f4_output_val == 0))
    {
        return;
    }

    f4_gain = motor_filter_limitf(p_st_trqvib_comp->f4_tf_lpf_time, 1.0f, 0.0f);
    f4_cos = cosf(f4_angle);
    f4_sin = sinf(f4_angle);

    f4_x = f4_input_val * f4_cos;
    f4_y = f4_input_val * f4_sin;

    p_st_trqvib_comp->f4_tf_lpf_buf_x += f4_gain * (f4_x - p_st_trqvib_comp->f4_tf_lpf_buf_x);
    p_st_trqvib_comp->f4_tf_lpf_buf_y += f4_gain * (f4_y - p_st_trqvib_comp->f4_tf_lpf_buf_y);

    *p_f4_output_val = (p_st_trqvib_comp->f4_tf_lpf_buf_x * f4_cos) + (p_st_trqvib_comp->f4_tf_lpf_buf_y * f4_sin);
}

float motor_current_trq_vibration_compensation_get_m_angle(st_trqvib_comp_target_t * p_st_trqvib_comp_target,
                                                           const float f4_angle_electric)
{
    if ((p_st_trqvib_comp_target == 0) || (p_st_trqvib_comp_target->u2_motor_pp == 0U))
    {
        return motor_trqvib_wrap_0_2pi(f4_angle_electric);
    }

    return motor_trqvib_wrap_0_2pi(f4_angle_electric / (float)p_st_trqvib_comp_target->u2_motor_pp);
}

void motor_current_trq_vibration_compensation_LU_generate(st_trqvib_comp_t *p_st_trqvib_comp)
{
    uint16_t u2_idx;
    float f4_avg = 0.0f;

    if (p_st_trqvib_comp == 0)
    {
        return;
    }

    for (u2_idx = 0U; u2_idx < TRQVIB_COMP_ARY_SIZE; u2_idx++)
    {
        f4_avg += p_st_trqvib_comp->st_trqvib_comp_1f.f4_repetitive_table[u2_idx];
    }
    f4_avg /= (float)TRQVIB_COMP_ARY_SIZE;

    memset(p_st_trqvib_comp->f4_repetitive_table, 0, sizeof(p_st_trqvib_comp->f4_repetitive_table));
    p_st_trqvib_comp->f4_repetitive_table[0] = f4_avg;
}

void motor_current_trq_vibration_compensation_main_PAT(st_trqvib_comp_t *p_st_trqvib_comp, st_trqvib_comp_target_t *p_st_trqvib_comp_target)
{
    float f4_phase;
    float f4_x;
    float f4_poly;
    uint16_t u2_idx;

    if ((p_st_trqvib_comp == 0) || (p_st_trqvib_comp_target == 0))
    {
        return;
    }

    f4_phase = motor_trqvib_wrap_pm_pi(p_st_trqvib_comp_target->f4_angle_rad_machine
                                      * p_st_trqvib_comp_target->f4_harmonic_order
                                      + p_st_trqvib_comp_target->f4_k2_timelead);
    f4_x = f4_phase / 3.1415926535f;
    f4_poly = 0.0f;

    for (u2_idx = 0U; u2_idx <= N_DIM; u2_idx++)
    {
        f4_poly = (f4_poly * f4_x) + p_st_trqvib_comp_target->f4_fitting_coeff[N_DIM - u2_idx];
    }

    if (p_st_trqvib_comp->u1_flag_trqvib_comp_learning == MTR_FLG_SET)
    {
        p_st_trqvib_comp_target->f4_fitting_coeff[0] +=
            0.01f * (p_st_trqvib_comp->f4_tf_output - p_st_trqvib_comp_target->f4_fitting_coeff[0]);
    }

    if ((p_st_trqvib_comp_target->u1_target_output_enable == MTR_FLG_SET)
        && (p_st_trqvib_comp->u2_trqcomp_state != TRQCOMP_STATE_IDLE))
    {
        p_st_trqvib_comp_target->f4_last_spd_comp = p_st_trqvib_comp_target->f4_output_gain * f4_poly;
    }
    else
    {
        p_st_trqvib_comp_target->f4_last_spd_comp = 0.0f;
    }
}

void motor_current_trq_vibration_compensation_learning_management(st_trqvib_comp_t *p_st_trqvib_comp,
                                                                  const float f4_angle_rad_machine_pre, const float f4_angle_rad_machine)
{
    uint8_t u1_cycle_crossed;

    if (p_st_trqvib_comp == 0)
    {
        return;
    }

    u1_cycle_crossed = (f4_angle_rad_machine < f4_angle_rad_machine_pre) ? MTR_FLG_SET : MTR_FLG_CLR;

    if ((p_st_trqvib_comp->u1_flag_trqvib_comp_learning == MTR_FLG_SET) && (u1_cycle_crossed == MTR_FLG_SET))
    {
        p_st_trqvib_comp->st_trqvib_comp_1f.u1_target_learning_done = MTR_FLG_SET;
        if (p_st_trqvib_comp->u1_target_2f == MTR_FLG_SET)
        {
            p_st_trqvib_comp->st_trqvib_comp_2f.u1_target_learning_done = MTR_FLG_SET;
        }
    }
}

void motor_current_trq_vibration_compensation_tvc_max_min_1cycle_update(st_trqvib_comp_t *p_st_trqvib_comp)
{
    if (p_st_trqvib_comp == 0)
    {
        return;
    }

    if (p_st_trqvib_comp->f4_tf_output > p_st_trqvib_comp->f4_tvc_max_1cycle)
    {
        p_st_trqvib_comp->f4_tvc_max_1cycle = p_st_trqvib_comp->f4_tf_output;
    }

    if (p_st_trqvib_comp->f4_tf_output < p_st_trqvib_comp->f4_tvc_min_1cycle)
    {
        p_st_trqvib_comp->f4_tvc_min_1cycle = p_st_trqvib_comp->f4_tf_output;
    }
}

void motor_current_trq_vibration_compensation_tvc_max_min_1cycle_reset(st_trqvib_comp_t *p_st_trqvib_comp)
{
    if (p_st_trqvib_comp == 0)
    {
        return;
    }

    p_st_trqvib_comp->f4_tvc_max_1cycle = -1.0e9f;
    p_st_trqvib_comp->f4_tvc_min_1cycle = 1.0e9f;
}

void motor_current_trq_vibration_compensation_auto_learning_on(st_trqvib_comp_t *p_st_trqvib_comp)
{
    if (p_st_trqvib_comp == 0)
    {
        return;
    }

    if ((p_st_trqvib_comp->u2_trqcomp_state == TRQCOMP_STATE_1F_STANDBY)
        || (p_st_trqvib_comp->u2_trqcomp_state == TRQCOMP_STATE_2F_STANDBY))
    {
        motor_current_trq_vibration_compensation_status_transision(p_st_trqvib_comp, TRQCOMP_ACTION_LEARNING_ON);
    }
}

void motor_current_trq_vibration_compensation_check_suppression_progress(st_trqvib_comp_t *p_st_trqvib_comp, const float f4_tf_target_th)
{
    float f4_amp_now;
    float f4_amp_ref;

    if (p_st_trqvib_comp == 0)
    {
        return;
    }

    f4_amp_now = fabsf(p_st_trqvib_comp->f4_tvc_max - p_st_trqvib_comp->f4_tvc_min);
    f4_amp_ref = fabsf(p_st_trqvib_comp->f4_tvc_amp_standby_pre);

    if ((f4_amp_ref > TRQVIB_MIN_PARAM) && (f4_amp_now <= (f4_amp_ref * f4_tf_target_th)))
    {
        motor_current_trq_vibration_compensation_status_transision(p_st_trqvib_comp, TRQCOMP_ACTION_LEARNING_OFF);
    }
}

void motor_current_trq_vibration_compensation_tvc_max_min_reset(st_trqvib_comp_t *p_st_trqvib_comp)
{
    if (p_st_trqvib_comp == 0)
    {
        return;
    }

    p_st_trqvib_comp->f4_tvc_max = -1.0e9f;
    p_st_trqvib_comp->f4_tvc_min = 1.0e9f;
    p_st_trqvib_comp->f4_tvc_amp_standby = 0.0f;
    p_st_trqvib_comp->f4_tvc_amp_standby_pre = 0.0f;
    p_st_trqvib_comp->f4_tvc_amp_learning = 0.0f;
}

void motor_current_trq_vibration_compensation_trackingfilter_reset(st_trqvib_comp_t *p_st_trqvib_comp)
{
    if (p_st_trqvib_comp == 0)
    {
        return;
    }

    p_st_trqvib_comp->f4_tf_lpf_buf_x = 0.0f;
    p_st_trqvib_comp->f4_tf_lpf_buf_y = 0.0f;
    p_st_trqvib_comp->f4_tf_output = 0.0f;
}

void motor_current_trq_vibration_compensation_tf_lpf_dc_calc(st_trqvib_comp_t *p_st_trqvib_comp, 
                                                                    const float f4_angle_rad_machine, const float f4_angle_rad_machine_pre)
{
    float f4_alpha;

    (void)f4_angle_rad_machine;
    (void)f4_angle_rad_machine_pre;

    if (p_st_trqvib_comp == 0)
    {
        return;
    }

    f4_alpha = motor_filter_limitf(p_st_trqvib_comp->f4_tf_lpf_time, 1.0f, 0.0f);
    p_st_trqvib_comp->f4_tf_lpf_x_dc += f4_alpha * (p_st_trqvib_comp->f4_tf_lpf_buf_x - p_st_trqvib_comp->f4_tf_lpf_x_dc);
    p_st_trqvib_comp->f4_tf_lpf_y_dc += f4_alpha * (p_st_trqvib_comp->f4_tf_lpf_buf_y - p_st_trqvib_comp->f4_tf_lpf_y_dc);
}

void motor_current_trq_vibration_compensation_tf_lpf_dc_reset(st_trqvib_comp_t *p_st_trqvib_comp)
{
    if (p_st_trqvib_comp == 0)
    {
        return;
    }

    p_st_trqvib_comp->f4_tf_lpf_x_dc = 0.0f;
    p_st_trqvib_comp->f4_tf_lpf_y_dc = 0.0f;
    p_st_trqvib_comp->f4_tf_lpf_x_ac = 0.0f;
    p_st_trqvib_comp->f4_tf_lpf_y_ac = 0.0f;

    p_st_trqvib_comp->f4_tf_lpf_x_max_1cycle = -1.0e9f;
    p_st_trqvib_comp->f4_tf_lpf_x_min_1cycle = 1.0e9f;
    p_st_trqvib_comp->f4_tf_lpf_y_max_1cycle = -1.0e9f;
    p_st_trqvib_comp->f4_tf_lpf_y_min_1cycle = 1.0e9f;
}

void motor_current_trq_vibration_compensation_tf_lpf_ac_calc(st_trqvib_comp_t *p_st_trqvib_comp, 
                                                                    const float f4_angle_rad_machine, const float f4_angle_rad_machine_pre)
{
    uint8_t u1_cycle_crossed;

    if (p_st_trqvib_comp == 0)
    {
        return;
    }

    if (p_st_trqvib_comp->f4_tf_lpf_buf_x > p_st_trqvib_comp->f4_tf_lpf_x_max_1cycle)
    {
        p_st_trqvib_comp->f4_tf_lpf_x_max_1cycle = p_st_trqvib_comp->f4_tf_lpf_buf_x;
    }
    if (p_st_trqvib_comp->f4_tf_lpf_buf_x < p_st_trqvib_comp->f4_tf_lpf_x_min_1cycle)
    {
        p_st_trqvib_comp->f4_tf_lpf_x_min_1cycle = p_st_trqvib_comp->f4_tf_lpf_buf_x;
    }
    if (p_st_trqvib_comp->f4_tf_lpf_buf_y > p_st_trqvib_comp->f4_tf_lpf_y_max_1cycle)
    {
        p_st_trqvib_comp->f4_tf_lpf_y_max_1cycle = p_st_trqvib_comp->f4_tf_lpf_buf_y;
    }
    if (p_st_trqvib_comp->f4_tf_lpf_buf_y < p_st_trqvib_comp->f4_tf_lpf_y_min_1cycle)
    {
        p_st_trqvib_comp->f4_tf_lpf_y_min_1cycle = p_st_trqvib_comp->f4_tf_lpf_buf_y;
    }

    u1_cycle_crossed = (f4_angle_rad_machine < f4_angle_rad_machine_pre) ? MTR_FLG_SET : MTR_FLG_CLR;
    if (u1_cycle_crossed == MTR_FLG_SET)
    {
        p_st_trqvib_comp->f4_tf_lpf_x_ac = fabsf(p_st_trqvib_comp->f4_tf_lpf_x_max_1cycle - p_st_trqvib_comp->f4_tf_lpf_x_min_1cycle);
        p_st_trqvib_comp->f4_tf_lpf_y_ac = fabsf(p_st_trqvib_comp->f4_tf_lpf_y_max_1cycle - p_st_trqvib_comp->f4_tf_lpf_y_min_1cycle);
        p_st_trqvib_comp->f4_tf_lpf_x_max_1cycle = -1.0e9f;
        p_st_trqvib_comp->f4_tf_lpf_x_min_1cycle = 1.0e9f;
        p_st_trqvib_comp->f4_tf_lpf_y_max_1cycle = -1.0e9f;
        p_st_trqvib_comp->f4_tf_lpf_y_min_1cycle = 1.0e9f;
    }
}

void motor_current_trq_vibration_compensation_check_tf_abnormal_output(st_trqvib_comp_t *p_st_trqvib_comp, const float f4_target_th)
{
    float f4_dc_mag;
    float f4_ac_mag;

    if (p_st_trqvib_comp == 0)
    {
        return;
    }

    f4_dc_mag = sqrtf((p_st_trqvib_comp->f4_tf_lpf_x_dc * p_st_trqvib_comp->f4_tf_lpf_x_dc)
                    + (p_st_trqvib_comp->f4_tf_lpf_y_dc * p_st_trqvib_comp->f4_tf_lpf_y_dc));
    f4_ac_mag = sqrtf((p_st_trqvib_comp->f4_tf_lpf_x_ac * p_st_trqvib_comp->f4_tf_lpf_x_ac)
                    + (p_st_trqvib_comp->f4_tf_lpf_y_ac * p_st_trqvib_comp->f4_tf_lpf_y_ac));

    if ((f4_ac_mag > TRQVIB_MIN_PARAM) && (f4_dc_mag > (f4_target_th * f4_ac_mag)))
    {
        motor_current_trq_vibration_compensation_status_transision(p_st_trqvib_comp, TRQCOMP_ACTION_LEARNING_OFF);
    }
}

void motor_current_trq_vibration_compensation_abnormal_prevention(st_trqvib_comp_t *p_st_trqvib_comp,
                                                                const float f4_angle_rad_machine_pre, const float f4_angle_rad_machine)
{
    if (p_st_trqvib_comp == 0)
    {
        return;
    }

    motor_current_trq_vibration_compensation_tvc_max_min_1cycle_update(p_st_trqvib_comp);
    motor_current_trq_vibration_compensation_tf_lpf_dc_calc(p_st_trqvib_comp,
                                                            f4_angle_rad_machine,
                                                            f4_angle_rad_machine_pre);
    motor_current_trq_vibration_compensation_tf_lpf_ac_calc(p_st_trqvib_comp,
                                                            f4_angle_rad_machine,
                                                            f4_angle_rad_machine_pre);
}

void motor_current_trq_vibration_compensation_status_transision(st_trqvib_comp_t *p_st_trqvib_comp, const uint16_t u2_action)
{
    if (p_st_trqvib_comp == 0)
    {
        return;
    }

    p_st_trqvib_comp->u2_trqcomp_action = u2_action;

    switch (u2_action)
    {
        case TRQCOMP_ACTION_RESET:
            motor_current_trq_vibration_compensation_reset(p_st_trqvib_comp);
            break;

        case TRQCOMP_ACTION_START:
            p_st_trqvib_comp->u2_trqcomp_state = TRQCOMP_STATE_1F_STANDBY;
            p_st_trqvib_comp->st_trqvib_comp_1f.u1_target_output_enable = MTR_FLG_SET;
            p_st_trqvib_comp->st_trqvib_comp_2f.u1_target_output_enable =
                (p_st_trqvib_comp->u1_target_2f == MTR_FLG_SET) ? MTR_FLG_SET : MTR_FLG_CLR;
            break;

        case TRQCOMP_ACTION_LEARNING_ON:
            p_st_trqvib_comp->u1_flag_trqvib_comp_learning = MTR_FLG_SET;
            if (p_st_trqvib_comp->u1_target_2f == MTR_FLG_SET)
            {
                p_st_trqvib_comp->u2_trqcomp_state = TRQCOMP_STATE_2F_LEARNING;
            }
            else
            {
                p_st_trqvib_comp->u2_trqcomp_state = TRQCOMP_STATE_1F_LEARNING;
            }
            break;

        case TRQCOMP_ACTION_LEARNING_OFF:
            p_st_trqvib_comp->u1_flag_trqvib_comp_learning = MTR_FLG_CLR;
            p_st_trqvib_comp->u2_trqcomp_state = TRQCOMP_STATE_COMPLETE;
            break;

        default:
            break;
    }
}

