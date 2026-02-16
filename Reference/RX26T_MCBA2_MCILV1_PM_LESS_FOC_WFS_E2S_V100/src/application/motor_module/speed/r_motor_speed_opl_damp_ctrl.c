/***********************************************************************************************************************
* File Name   : r_motor_speed_opl_damp_ctrl.c
* Description : Open C implementation of open-loop damping controller.
***********************************************************************************************************************/

#include <math.h>

#include "r_motor_speed_opl_damp_ctrl.h"
#include "r_motor_filter.h"
#include "r_motor_common.h"

#define OPL_DAMP_MIN_PARAM             (1.0e-6f)

float motor_speed_opl_damp_ctrl(st_opl_damp_t * p_st_opl_damp, float f4_ed, float f4_speed_ref)
{
    float f4_ed_filtered;
    float f4_comp_speed;
    float f4_limit_abs;

    if (p_st_opl_damp == 0)
    {
        return 0.0f;
    }

    f4_ed_filtered = motor_filter_first_order_lpff(&p_st_opl_damp->st_ed_lpf, f4_ed);
    f4_comp_speed = p_st_opl_damp->f4_damp_comp_gain * f4_ed_filtered;
    f4_limit_abs = fabsf(f4_speed_ref) * p_st_opl_damp->f4_fb_speed_limit_rate;

    return motor_filter_limitf_abs(f4_comp_speed, f4_limit_abs);
}

void motor_speed_opl_damp_init(st_opl_damp_t * p_st_opl_damp, float f4_fb_speed_limit_rate)
{
    if (p_st_opl_damp == 0)
    {
        return;
    }

    motor_filter_first_order_lpff_init(&p_st_opl_damp->st_ed_lpf);
    p_st_opl_damp->f4_damp_comp_gain = 0.0f;
    p_st_opl_damp->f4_fb_speed_limit_rate = motor_filter_limitf(f4_fb_speed_limit_rate, 1.0f, 0.0f);
}

void motor_speed_opl_damp_reset(st_opl_damp_t * p_st_opl_damp)
{
    if (p_st_opl_damp == 0)
    {
        return;
    }

    motor_filter_first_order_lpff_reset(&p_st_opl_damp->st_ed_lpf);
}

void motor_speed_opl_damp_gain_set(st_opl_damp_t * p_st_opl_damp,
                                   float f4_damp_gain,
                                   float f4_omega_t,
                                   float f4_gain_ka,
                                   float f4_gain_kb)
{
    if (p_st_opl_damp == 0)
    {
        return;
    }

    p_st_opl_damp->f4_damp_comp_gain = f4_damp_gain;
    p_st_opl_damp->st_ed_lpf.f4_omega_t = f4_omega_t;

    if ((fabsf(f4_gain_ka) <= OPL_DAMP_MIN_PARAM) && (fabsf(f4_gain_kb) <= OPL_DAMP_MIN_PARAM))
    {
        p_st_opl_damp->st_ed_lpf.f4_gain_ka = (2.0f - f4_omega_t) / (2.0f + f4_omega_t);
        p_st_opl_damp->st_ed_lpf.f4_gain_kb = f4_omega_t / (2.0f + f4_omega_t);
    }
    else
    {
        p_st_opl_damp->st_ed_lpf.f4_gain_ka = f4_gain_ka;
        p_st_opl_damp->st_ed_lpf.f4_gain_kb = f4_gain_kb;
    }
}

void motor_speed_opl_damp_r_gain_set(st_opl_damp_t * p_st_opl_damp,
                                     uint16_t u2_pp,
                                     float f4_ke,
                                     float f4_j,
                                     float f4_zeta,
                                     float f4_ed_hpf_fc,
                                     float f4_opl_current,
                                     float f4_id_down_speed,
                                     float f4_tc)
{
    float f4_omega_t;
    float f4_ka;
    float f4_kb;
    float f4_gain_den;
    float f4_damp_gain;

    if (p_st_opl_damp == 0)
    {
        return;
    }

    f4_omega_t = MTR_TWOPI * fmaxf(f4_ed_hpf_fc, 0.0f) * fmaxf(f4_tc, OPL_DAMP_MIN_PARAM);
    f4_ka = (2.0f - f4_omega_t) / (2.0f + f4_omega_t);
    f4_kb = f4_omega_t / (2.0f + f4_omega_t);

    f4_gain_den = fmaxf((float)u2_pp * fabsf(f4_ke) * fmaxf(f4_j, OPL_DAMP_MIN_PARAM), OPL_DAMP_MIN_PARAM);
    f4_damp_gain = (f4_zeta * (fabsf(f4_opl_current) + fabsf(f4_id_down_speed) * fmaxf(f4_tc, OPL_DAMP_MIN_PARAM)))
                   / f4_gain_den;

    motor_speed_opl_damp_gain_set(p_st_opl_damp, f4_damp_gain, f4_omega_t, f4_ka, f4_kb);
}

void motor_speed_opl_damp_limit_set(st_opl_damp_t * p_st_opl_damp, float f4_limit_rate)
{
    if (p_st_opl_damp == 0)
    {
        return;
    }

    p_st_opl_damp->f4_fb_speed_limit_rate = motor_filter_limitf(f4_limit_rate, 1.0f, 0.0f);
}

