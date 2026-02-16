/***********************************************************************************************************************
* File Name   : r_motor_sensorless_vector_flyingstart.c
* Description : Open C implementation of flying-start sequence.
***********************************************************************************************************************/

#include <math.h>
#include <string.h>

#include "r_motor_module_cfg.h"
#include "r_motor_sensorless_vector_flyingstart.h"

st_flying_start_t g_st_flying_start;

static float motor_flying_start_wrap_pm_pi(float f4_angle)
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

static void motor_flying_start_clarke_transform(st_flying_start_t *p_st_flystart)
{
    p_st_flystart->f4_ia_ad = p_st_flystart->f4_iu_ad;
    p_st_flystart->f4_ib_ad = (p_st_flystart->f4_iu_ad + (2.0f * p_st_flystart->f4_iv_ad)) / MTR_SQRT_3;
}

static void motor_flying_start_dq_transform(st_flying_start_t *p_st_flystart, float f4_angle)
{
    float f4_sin = sinf(f4_angle);
    float f4_cos = cosf(f4_angle);

    p_st_flystart->f4_id_ad = (p_st_flystart->f4_ia_ad * f4_cos) + (p_st_flystart->f4_ib_ad * f4_sin);
    p_st_flystart->f4_iq_ad = (-p_st_flystart->f4_ia_ad * f4_sin) + (p_st_flystart->f4_ib_ad * f4_cos);
}

void motor_flying_start_init(st_flying_start_t *p_st_flystart)
{
    st_flying_start_cfg_t st_cfg;

    if (p_st_flystart == 0)
    {
        return;
    }

    memset(p_st_flystart, 0, sizeof(st_flying_start_t));

    st_cfg.f4_ctrl_period = MOTOR_COMMON_CTRL_PERIOD;
    st_cfg.f4_restart_speed = FLY_START_DEFAULT_RESTART_SPEED_LIMIT * MTR_RPM2RAD;
    st_cfg.f4_off_time = FLY_START_DEFAULT_OFF_TIME_SEC;
    st_cfg.f4_on_current_th = FLY_START_DEFAULT_CURRENT_TH;
    st_cfg.u2_off_time_cnt = (uint16_t)(FLY_START_DEFAULT_OFF_TIME_SEC / MOTOR_COMMON_CTRL_PERIOD);
    st_cfg.u2_over_time_cnt = (uint16_t)(FLY_START_DEFAULT_OVER_TIME_SEC / MOTOR_COMMON_CTRL_PERIOD);
    st_cfg.u2_active_brake_time_cnt = (uint16_t)(FLY_START_DEFAULT_ACTIVE_BRAKE_TIME_SEC / MOTOR_COMMON_CTRL_PERIOD);
    st_cfg.p_st_motor = 0;

    motor_flying_start_parameter_set(p_st_flystart, &st_cfg);
    motor_flying_start_reset(p_st_flystart);
}

void motor_flying_start_reset(st_flying_start_t *p_st_flystart)
{
    float f4_ctrl_period;
    float f4_restart_speed;
    float f4_off_time;
    float f4_on_current_th;
    uint16_t u2_off_time_cnt;
    uint16_t u2_over_time_cnt;
    uint16_t u2_active_brake_time_cnt;
    st_motor_parameter_t * p_st_motor;

    if (p_st_flystart == 0)
    {
        return;
    }

    /* Keep configured values across reset and clear only runtime state. */
    f4_ctrl_period = p_st_flystart->f4_ctrl_period;
    f4_restart_speed = p_st_flystart->f4_restart_speed;
    f4_off_time = p_st_flystart->f4_off_time;
    f4_on_current_th = p_st_flystart->f4_on_current_th;
    u2_off_time_cnt = p_st_flystart->u2_off_time_cnt;
    u2_over_time_cnt = p_st_flystart->u2_over_time_cnt;
    u2_active_brake_time_cnt = p_st_flystart->u2_active_brake_time_cnt;
    p_st_motor = p_st_flystart->p_st_motor;

    memset(p_st_flystart, 0, sizeof(st_flying_start_t));

    p_st_flystart->f4_ctrl_period = f4_ctrl_period;
    p_st_flystart->f4_restart_speed = f4_restart_speed;
    p_st_flystart->f4_off_time = f4_off_time;
    p_st_flystart->f4_on_current_th = f4_on_current_th;
    p_st_flystart->u2_off_time_cnt = u2_off_time_cnt;
    p_st_flystart->u2_over_time_cnt = u2_over_time_cnt;
    p_st_flystart->u2_active_brake_time_cnt = u2_active_brake_time_cnt;
    p_st_flystart->p_st_motor = p_st_motor;

    p_st_flystart->u1_state = FLY_STATE_IDLE;
    p_st_flystart->u1_action = FLY_ACTION_IDLE;
}

void motor_flying_start_parameter_set(st_flying_start_t *p_st_flystart, const st_flying_start_cfg_t *p_st_flystart_cfg)
{
    if ((p_st_flystart == 0) || (p_st_flystart_cfg == 0))
    {
        return;
    }

    p_st_flystart->f4_ctrl_period = fmaxf(p_st_flystart_cfg->f4_ctrl_period, MOTOR_COMMON_CTRL_PERIOD);
    p_st_flystart->f4_restart_speed = p_st_flystart_cfg->f4_restart_speed;
    p_st_flystart->f4_off_time = p_st_flystart_cfg->f4_off_time;
    p_st_flystart->f4_on_current_th = fmaxf(p_st_flystart_cfg->f4_on_current_th, 0.0f);
    p_st_flystart->u2_off_time_cnt = (uint16_t)fmaxf((float)p_st_flystart_cfg->u2_off_time_cnt, 1.0f);
    p_st_flystart->u2_over_time_cnt = (uint16_t)fmaxf((float)p_st_flystart_cfg->u2_over_time_cnt, 1.0f);
    p_st_flystart->u2_active_brake_time_cnt = (uint16_t)fmaxf((float)p_st_flystart_cfg->u2_active_brake_time_cnt, 1.0f);
    p_st_flystart->p_st_motor = p_st_flystart_cfg->p_st_motor;
}

void motor_flying_start_main(st_flying_start_t *p_st_flystart)
{
    float f4_dt;
    float f4_angle_delta;
    float f4_angle_meas;

    if (p_st_flystart == 0)
    {
        return;
    }

    motor_flying_start_clarke_transform(p_st_flystart);
    p_st_flystart->f4_current_mag = sqrtf((p_st_flystart->f4_ia_ad * p_st_flystart->f4_ia_ad)
                                        + (p_st_flystart->f4_ib_ad * p_st_flystart->f4_ib_ad));

    p_st_flystart->u1_action = FLY_ACTION_IDLE;

    switch (p_st_flystart->u1_state)
    {
        case FLY_STATE_IDLE:
            p_st_flystart->u1_state = FLY_STATE_FIRST_ON;
            p_st_flystart->u2_time_cnt = 0U;
            p_st_flystart->u1_action = FLY_ACTION_SET_DUTY;
            break;

        case FLY_STATE_FIRST_ON:
            p_st_flystart->u2_time_cnt++;
            p_st_flystart->u1_action = FLY_ACTION_PWM_OUTPUT_ENABLE;

            if ((p_st_flystart->f4_current_mag >= p_st_flystart->f4_on_current_th)
                || (p_st_flystart->u2_time_cnt >= p_st_flystart->u2_over_time_cnt))
            {
                p_st_flystart->f4_ia_ad1st = p_st_flystart->f4_ia_ad;
                p_st_flystart->f4_ib_ad1st = p_st_flystart->f4_ib_ad;
                p_st_flystart->f4_angle1st = atan2f(p_st_flystart->f4_ib_ad1st, p_st_flystart->f4_ia_ad1st);

                p_st_flystart->u1_state = FLY_STATE_FIRST_OFF;
                p_st_flystart->u2_time_cnt = 0U;
                p_st_flystart->u1_action = FLY_ACTION_PWM_OUTPUT_DISABLE;
            }
            break;

        case FLY_STATE_FIRST_OFF:
            p_st_flystart->u2_time_cnt++;
            p_st_flystart->u1_action = FLY_ACTION_PWM_OUTPUT_DISABLE;

            if (p_st_flystart->u2_time_cnt >= p_st_flystart->u2_off_time_cnt)
            {
                p_st_flystart->u1_state = FLY_STATE_SECOND_ON;
                p_st_flystart->u2_time_cnt = 0U;
                p_st_flystart->u1_action = FLY_ACTION_PWM_OUTPUT_ENABLE;
            }
            break;

        case FLY_STATE_SECOND_ON:
            p_st_flystart->u2_time_cnt++;
            p_st_flystart->u1_action = FLY_ACTION_PWM_OUTPUT_ENABLE;

            if ((p_st_flystart->f4_current_mag >= p_st_flystart->f4_on_current_th)
                || (p_st_flystart->u2_time_cnt >= p_st_flystart->u2_over_time_cnt))
            {
                p_st_flystart->f4_ia_ad2nd = p_st_flystart->f4_ia_ad;
                p_st_flystart->f4_ib_ad2nd = p_st_flystart->f4_ib_ad;
                p_st_flystart->f4_angle2nd = atan2f(p_st_flystart->f4_ib_ad2nd, p_st_flystart->f4_ia_ad2nd);

                p_st_flystart->u1_state = FLY_STATE_SECOND_OFF;
                p_st_flystart->u2_time_cnt = 0U;
                p_st_flystart->u1_action = FLY_ACTION_PWM_OUTPUT_DISABLE;
            }
            break;

        case FLY_STATE_SECOND_OFF:
            p_st_flystart->u2_time_cnt++;
            p_st_flystart->u1_action = FLY_ACTION_PWM_OUTPUT_DISABLE;

            if (p_st_flystart->u2_time_cnt >= p_st_flystart->u2_off_time_cnt)
            {
                f4_dt = fmaxf(p_st_flystart->f4_off_time, p_st_flystart->f4_ctrl_period);
                f4_angle_delta = motor_flying_start_wrap_pm_pi(p_st_flystart->f4_angle2nd - p_st_flystart->f4_angle1st);

                p_st_flystart->f4_omega_e_ini = f4_angle_delta / f4_dt;
                if ((p_st_flystart->p_st_motor != 0) && (p_st_flystart->p_st_motor->u2_mtr_pp > 0U))
                {
                    p_st_flystart->f4_omega_m_ini = p_st_flystart->f4_omega_e_ini / (float)p_st_flystart->p_st_motor->u2_mtr_pp;
                }
                else
                {
                    p_st_flystart->f4_omega_m_ini = p_st_flystart->f4_omega_e_ini;
                }

                f4_angle_meas = p_st_flystart->f4_angle2nd;
                p_st_flystart->f4_angle_e_ini = f4_angle_meas;
                p_st_flystart->f4_angle_rotate = f4_angle_meas;

                motor_flying_start_dq_transform(p_st_flystart, p_st_flystart->f4_angle_e_ini);

                if (p_st_flystart->p_st_motor != 0)
                {
                    p_st_flystart->f4_emf_volt = fabsf(p_st_flystart->f4_omega_e_ini) * p_st_flystart->p_st_motor->f4_mtr_m;
                }
                else
                {
                    p_st_flystart->f4_emf_volt = 0.0f;
                }

                if (fabsf(p_st_flystart->f4_omega_m_ini) >= p_st_flystart->f4_restart_speed)
                {
                    p_st_flystart->u1_action = FLY_ACTION_CTRL_PARAM_SET;
                }
                else
                {
                    p_st_flystart->u1_action = FLY_ACTION_NORMAL_START;
                }

                p_st_flystart->u1_state = FLY_STATE_COMPLETE;
                p_st_flystart->u2_time_cnt = 0U;
            }
            break;

        case FLY_STATE_ACTIVE_BRAKE:
            p_st_flystart->u2_time_cnt++;
            p_st_flystart->u1_action = FLY_ACTION_PWM_OUTPUT_DISABLE;
            if (p_st_flystart->u2_time_cnt >= p_st_flystart->u2_active_brake_time_cnt)
            {
                p_st_flystart->u1_state = FLY_STATE_COMPLETE;
                p_st_flystart->u1_action = FLY_ACTION_NORMAL_START;
            }
            break;

        case FLY_STATE_COMPLETE:
        case FLY_STATE_CTRL_START:
        default:
            p_st_flystart->u1_action = FLY_ACTION_IDLE;
            break;
    }
}
