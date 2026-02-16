/***********************************************************************************************************************
* File Name   : r_motor_speed_opl2less.c
* Description : Open C implementation of sensor-less switching helpers.
***********************************************************************************************************************/

#include <math.h>

#include "r_motor_speed_opl2less.h"
#include "r_motor_filter.h"

#define OPL2LESS_MIN_PARAM             (1.0e-6f)

float motor_speed_opl2less_iq_calc(float f4_ed,
                                   float f4_eq,
                                   float f4_id,
                                   float f4_torque_current,
                                   float f4_phase_err)
{
    float f4_phase_cos;
    float f4_phase_sin;
    float f4_bemf_aligned;
    float f4_iq_ref;

    f4_phase_cos = cosf(f4_phase_err);
    f4_phase_sin = sinf(f4_phase_err);

    /* Blend torque preload with BEMF alignment to keep the switching phase smooth. */
    f4_bemf_aligned = (f4_eq * f4_phase_cos) - (f4_ed * f4_phase_sin);
    f4_iq_ref = f4_torque_current + (0.25f * f4_bemf_aligned) - (0.10f * f4_id * f4_phase_sin);

    if (f4_torque_current >= 0.0f)
    {
        f4_iq_ref = motor_filter_lower_limitf(f4_iq_ref, 0.0f);
    }
    else
    {
        f4_iq_ref = motor_filter_upper_limitf(f4_iq_ref, 0.0f);
    }

    return f4_iq_ref;
}

float motor_speed_opl2less_torque_current_calc(const st_motor_parameter_t * p_st_motor,
                                               float f4_opl2less_sw_time,
                                               float f4_ol_id_ref,
                                               float f4_phase_err_rad_lpf)
{
    float f4_sw_time;
    float f4_phase_rate;
    float f4_torque_need;
    float f4_torque_const;
    float f4_iq_calc;
    float f4_iq_limit;
    float f4_bias;

    if (p_st_motor == 0)
    {
        return 0.0f;
    }

    f4_sw_time = fmaxf(f4_opl2less_sw_time, OPL2LESS_MIN_PARAM);
    f4_phase_rate = f4_phase_err_rad_lpf / f4_sw_time;
    f4_torque_need = p_st_motor->f4_mtr_j * (f4_phase_rate / f4_sw_time);
    f4_torque_const = 1.5f * (float)p_st_motor->u2_mtr_pp * fmaxf(p_st_motor->f4_mtr_m, OPL2LESS_MIN_PARAM);

    f4_iq_calc = f4_torque_need / f4_torque_const;

    /* Keep a small preload contribution from open-loop Id so the transition does not lose torque. */
    f4_bias = 0.10f * fabsf(f4_ol_id_ref);
    if (f4_phase_err_rad_lpf < 0.0f)
    {
        f4_iq_calc -= f4_bias;
    }
    else
    {
        f4_iq_calc += f4_bias;
    }

    f4_iq_limit = fmaxf(p_st_motor->f4_nominal_current_rms * MTR_SQRT_3, 0.1f);
    f4_iq_calc = motor_filter_limitf_abs(f4_iq_calc, f4_iq_limit);

    return f4_iq_calc;
}

