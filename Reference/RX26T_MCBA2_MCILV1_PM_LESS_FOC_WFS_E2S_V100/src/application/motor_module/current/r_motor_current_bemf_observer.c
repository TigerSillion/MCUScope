/***********************************************************************************************************************
* File Name   : r_motor_current_bemf_observer.c
* Description : Open C implementation of BEMF observer and PLL speed estimation.
***********************************************************************************************************************/

#include <math.h>

#include "r_motor_current_bemf_observer.h"
#include "r_motor_filter.h"

#define MOTOR_BEMF_MIN_PARAM            (1.0e-6f)
#define MOTOR_BEMF_PLL_SPEED_LIMIT      (20000.0f)

static float motor_bemf_limitf_abs(float value, float limit)
{
    if (limit <= 0.0f)
    {
        return value;
    }

    return motor_filter_limitf_abs(value, limit);
}

void motor_current_bemf_observer_start(st_bemf_observer_t * p_st_bemf_observer,
                                       float f4_vd_ref,
                                       float f4_vq_ref,
                                       float f4_id,
                                       float f4_iq)
{
    if (p_st_bemf_observer == 0)
    {
        return;
    }

    p_st_bemf_observer->st_d_axis.f4_i_pre = f4_id;
    p_st_bemf_observer->st_q_axis.f4_i_pre = f4_iq;

    /* The observer is driven by disturbance states, so keep previous estimates coherent with the latest command. */
    p_st_bemf_observer->st_d_axis.f4_d_est_pre +=
        (f4_vd_ref - p_st_bemf_observer->st_d_axis.f4_d_est_pre) * 0.1f;
    p_st_bemf_observer->st_q_axis.f4_d_est_pre +=
        (f4_vq_ref - p_st_bemf_observer->st_q_axis.f4_d_est_pre) * 0.1f;
}

float motor_current_bemf_observer_d_calc(st_bemf_observer_t * p_st_bemf_observer, float f4_speed_rad, float f4_iq)
{
    float f4_r;
    float f4_ld;
    float f4_lq;
    float f4_dt;
    float f4_i_est;
    float f4_err;
    float f4_d_est;

    if ((p_st_bemf_observer == 0) || (p_st_bemf_observer->p_st_motor == 0))
    {
        return 0.0f;
    }

    f4_r = p_st_bemf_observer->p_st_motor->f4_mtr_r;
    f4_ld = fmaxf(p_st_bemf_observer->p_st_motor->f4_mtr_ld, MOTOR_BEMF_MIN_PARAM);
    f4_lq = fmaxf(p_st_bemf_observer->p_st_motor->f4_mtr_lq, MOTOR_BEMF_MIN_PARAM);
    f4_dt = fmaxf(p_st_bemf_observer->f4_dt, MOTOR_BEMF_MIN_PARAM);

    f4_i_est = p_st_bemf_observer->st_d_axis.f4_i_est_pre
               + (f4_dt / f4_ld)
                 * ((-f4_r * p_st_bemf_observer->st_d_axis.f4_i_est_pre)
                    + (f4_speed_rad * f4_lq * f4_iq)
                    + p_st_bemf_observer->st_d_axis.f4_d_est_pre);

    f4_err = p_st_bemf_observer->st_d_axis.f4_i_pre - f4_i_est;

    p_st_bemf_observer->st_d_axis.f4_i_est_pre = f4_i_est + (p_st_bemf_observer->st_d_axis.f4_k_e_obs_1 * f4_err);

    f4_d_est = p_st_bemf_observer->st_d_axis.f4_d_est_pre
               + (p_st_bemf_observer->st_d_axis.f4_k_e_obs_2 * f4_err);
    f4_d_est = motor_bemf_limitf_abs(f4_d_est, p_st_bemf_observer->st_d_axis.f4_d_est_limit);

    p_st_bemf_observer->st_d_axis.f4_d_est_pre = f4_d_est;
    p_st_bemf_observer->st_d_axis.f4_d_est = f4_d_est;

    return f4_d_est;
}

float motor_current_bemf_observer_q_calc(st_bemf_observer_t * p_st_bemf_observer, float f4_speed_rad, float f4_id)
{
    float f4_r;
    float f4_ld;
    float f4_lq;
    float f4_flux;
    float f4_dt;
    float f4_i_est;
    float f4_err;
    float f4_d_est;

    if ((p_st_bemf_observer == 0) || (p_st_bemf_observer->p_st_motor == 0))
    {
        return 0.0f;
    }

    f4_r = p_st_bemf_observer->p_st_motor->f4_mtr_r;
    f4_ld = fmaxf(p_st_bemf_observer->p_st_motor->f4_mtr_ld, MOTOR_BEMF_MIN_PARAM);
    f4_lq = fmaxf(p_st_bemf_observer->p_st_motor->f4_mtr_lq, MOTOR_BEMF_MIN_PARAM);
    f4_flux = p_st_bemf_observer->p_st_motor->f4_mtr_m;
    f4_dt = fmaxf(p_st_bemf_observer->f4_dt, MOTOR_BEMF_MIN_PARAM);

    f4_i_est = p_st_bemf_observer->st_q_axis.f4_i_est_pre
               + (f4_dt / f4_lq)
                 * ((-f4_r * p_st_bemf_observer->st_q_axis.f4_i_est_pre)
                    - (f4_speed_rad * ((f4_ld * f4_id) + f4_flux))
                    + p_st_bemf_observer->st_q_axis.f4_d_est_pre);

    f4_err = p_st_bemf_observer->st_q_axis.f4_i_pre - f4_i_est;

    p_st_bemf_observer->st_q_axis.f4_i_est_pre = f4_i_est + (p_st_bemf_observer->st_q_axis.f4_k_e_obs_1 * f4_err);

    f4_d_est = p_st_bemf_observer->st_q_axis.f4_d_est_pre
               + (p_st_bemf_observer->st_q_axis.f4_k_e_obs_2 * f4_err);
    f4_d_est = motor_bemf_limitf_abs(f4_d_est, p_st_bemf_observer->st_q_axis.f4_d_est_limit);

    p_st_bemf_observer->st_q_axis.f4_d_est_pre = f4_d_est;
    p_st_bemf_observer->st_q_axis.f4_d_est = f4_d_est;

    return f4_d_est;
}

float motor_current_bemf_observer_amp_calc(float f4_ed, float f4_eq)
{
    return sqrtf((f4_ed * f4_ed) + (f4_eq * f4_eq));
}

void motor_current_bemf_observer_init(st_bemf_observer_t * p_st_bemf_observer, st_motor_parameter_t * p_st_motor)
{
    if (p_st_bemf_observer == 0)
    {
        return;
    }

    p_st_bemf_observer->f4_dt = 0.0f;
    p_st_bemf_observer->p_st_motor = p_st_motor;

    p_st_bemf_observer->st_d_axis.f4_k_e_obs_1 = 0.0f;
    p_st_bemf_observer->st_d_axis.f4_k_e_obs_2 = 0.0f;
    p_st_bemf_observer->st_q_axis.f4_k_e_obs_1 = 0.0f;
    p_st_bemf_observer->st_q_axis.f4_k_e_obs_2 = 0.0f;

    motor_current_bemf_observer_reset(p_st_bemf_observer);
}

void motor_current_bemf_observer_gain_calc(st_bemf_observer_t * p_st_bemf_observer,
                                           st_motor_parameter_t * p_st_motor,
                                           float f4_e_obs_omega_hz,
                                           float f4_e_obs_zeta,
                                           float f4_ctrl_period)
{
    float f4_omega_rad;
    float f4_ld;
    float f4_lq;
    float f4_k1_d;
    float f4_k2_d;
    float f4_k1_q;
    float f4_k2_q;

    if ((p_st_bemf_observer == 0) || (p_st_motor == 0))
    {
        return;
    }

    f4_omega_rad = MTR_TWOPI * fmaxf(f4_e_obs_omega_hz, 0.0f);
    f4_ld = fmaxf(p_st_motor->f4_mtr_ld, MOTOR_BEMF_MIN_PARAM);
    f4_lq = fmaxf(p_st_motor->f4_mtr_lq, MOTOR_BEMF_MIN_PARAM);

    f4_k1_d = 2.0f * f4_e_obs_zeta * f4_omega_rad * f4_ctrl_period;
    f4_k2_d = f4_omega_rad * f4_omega_rad * f4_ctrl_period * f4_ctrl_period * f4_ld;
    f4_k1_q = 2.0f * f4_e_obs_zeta * f4_omega_rad * f4_ctrl_period;
    f4_k2_q = f4_omega_rad * f4_omega_rad * f4_ctrl_period * f4_ctrl_period * f4_lq;

    motor_current_bemf_observer_gain_set(p_st_bemf_observer,
                                         f4_ctrl_period,
                                         f4_k1_d,
                                         f4_k2_d,
                                         f4_k1_q,
                                         f4_k2_q);
    p_st_bemf_observer->p_st_motor = p_st_motor;
}

void motor_current_bemf_observer_gain_set(st_bemf_observer_t * p_st_bemf_observer,
                                          float f4_dt,
                                          float f4_k1_d,
                                          float f4_k2_d,
                                          float f4_k1_q,
                                          float f4_k2_q)
{
    if (p_st_bemf_observer == 0)
    {
        return;
    }

    p_st_bemf_observer->f4_dt = fmaxf(f4_dt, MOTOR_BEMF_MIN_PARAM);
    p_st_bemf_observer->st_d_axis.f4_k_e_obs_1 = f4_k1_d;
    p_st_bemf_observer->st_d_axis.f4_k_e_obs_2 = f4_k2_d;
    p_st_bemf_observer->st_q_axis.f4_k_e_obs_1 = f4_k1_q;
    p_st_bemf_observer->st_q_axis.f4_k_e_obs_2 = f4_k2_q;
}

void motor_current_bemf_observer_reset(st_bemf_observer_t * p_st_bemf_observer)
{
    if (p_st_bemf_observer == 0)
    {
        return;
    }

    p_st_bemf_observer->st_d_axis.f4_i_pre = 0.0f;
    p_st_bemf_observer->st_d_axis.f4_i_est_pre = 0.0f;
    p_st_bemf_observer->st_d_axis.f4_d_est = 0.0f;
    p_st_bemf_observer->st_d_axis.f4_d_est_pre = 0.0f;

    p_st_bemf_observer->st_q_axis.f4_i_pre = 0.0f;
    p_st_bemf_observer->st_q_axis.f4_i_est_pre = 0.0f;
    p_st_bemf_observer->st_q_axis.f4_d_est = 0.0f;
    p_st_bemf_observer->st_q_axis.f4_d_est_pre = 0.0f;
}

void motor_current_angle_speed_pll(st_pll_est_t * p_st_pll_est, float f4_phase_err, float * f4_speed)
{
    float f4_speed_temp;

    if ((p_st_pll_est == 0) || (f4_speed == 0))
    {
        return;
    }

    p_st_pll_est->f4_i_est_speed += p_st_pll_est->f4_ki_est_speed * f4_phase_err;
    p_st_pll_est->f4_i_est_speed = motor_filter_limitf_abs(p_st_pll_est->f4_i_est_speed, MOTOR_BEMF_PLL_SPEED_LIMIT);

    f4_speed_temp = p_st_pll_est->f4_i_est_speed + (p_st_pll_est->f4_kp_est_speed * f4_phase_err);
    *f4_speed = motor_filter_limitf_abs(f4_speed_temp, MOTOR_BEMF_PLL_SPEED_LIMIT);
}

void motor_current_angle_speed_init(st_pll_est_t * p_st_pll_est)
{
    if (p_st_pll_est == 0)
    {
        return;
    }

    p_st_pll_est->f4_kp_est_speed = 0.0f;
    p_st_pll_est->f4_ki_est_speed = 0.0f;
    p_st_pll_est->f4_i_est_speed = 0.0f;
}

void motor_current_angle_speed_gain_calc(st_pll_est_t * p_st_pll_est,
                                         float f4_pll_est_omega_hz,
                                         float f4_pll_est_zeta,
                                         float f4_ctrl_period)
{
    float f4_omega_rad;
    float f4_kp;
    float f4_ki;

    if (p_st_pll_est == 0)
    {
        return;
    }

    f4_omega_rad = MTR_TWOPI * fmaxf(f4_pll_est_omega_hz, 0.0f);
    f4_kp = 2.0f * f4_pll_est_zeta * f4_omega_rad;
    f4_ki = f4_omega_rad * f4_omega_rad * f4_ctrl_period;

    motor_current_angle_speed_gain_set(p_st_pll_est, f4_kp, f4_ki);
}

void motor_current_angle_speed_gain_set(st_pll_est_t * p_st_pll_est, float f4_kp, float f4_ki)
{
    if (p_st_pll_est == 0)
    {
        return;
    }

    p_st_pll_est->f4_kp_est_speed = f4_kp;
    p_st_pll_est->f4_ki_est_speed = f4_ki;
}

void motor_current_angle_speed_reset(st_pll_est_t * p_st_pll_est)
{
    if (p_st_pll_est == 0)
    {
        return;
    }

    p_st_pll_est->f4_i_est_speed = 0.0f;
}

