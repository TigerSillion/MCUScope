/***********************************************************************************************************************
* File Name   : r_motor_speed_fluxwkn.c
* Description : Open C implementation of flux-weakening module.
***********************************************************************************************************************/

#include <math.h>

#include "r_motor_speed_fluxwkn.h"
#include "r_motor_filter.h"

#define FLUXWKN_MIN_PARAM              (1.0e-6f)

static float motor_speed_fluxwkn_minf(float a, float b)
{
    return (a < b) ? a : b;
}

void motor_speed_flux_weakn_init(st_fluxwkn_t * p_st_fluxwkn,
                                 float f4_ia_max,
                                 float f4_va_max,
                                 float f4_vfw_ratio,
                                 const st_motor_parameter_t *p_st_motor)
{
    if (p_st_fluxwkn == 0)
    {
        return;
    }

    p_st_fluxwkn->p_motor = p_st_motor;
    p_st_fluxwkn->f4_ia_max = f4_ia_max;
    p_st_fluxwkn->f4_va_max = f4_va_max;
    p_st_fluxwkn->f4_vfw_ratio = f4_vfw_ratio;

    motor_speed_flux_weakn_reset(p_st_fluxwkn);
}

void motor_speed_flux_weakn_reset(st_fluxwkn_t * p_st_fluxwkn)
{
    if (p_st_fluxwkn == 0)
    {
        return;
    }

    p_st_fluxwkn->f4_id_demag = 0.0f;
    p_st_fluxwkn->f4_id_min = 0.0f;
    p_st_fluxwkn->f4_v_fw = 0.0f;
    p_st_fluxwkn->u2_fw_status = FLUXWKN_STATE_BYPASSED;
}

uint16_t motor_speed_flux_weakn_start(st_fluxwkn_t * p_st_fluxwkn,
                                      float f4_speed_rad,
                                      const float *p_f4_idq,
                                      float *p_f4_idq_ref)
{
    float f4_speed_abs;
    float f4_ld;
    float f4_flux;
    float f4_flux_limit;
    float f4_id_target;
    float f4_iq_max;
    float f4_iq_sign;

    if ((p_st_fluxwkn == 0) || (p_f4_idq == 0) || (p_f4_idq_ref == 0))
    {
        return FLUXWKN_STATE_ERROR;
    }

    if (motor_speed_flux_weakn_error_check(p_st_fluxwkn) != MTR_FALSE)
    {
        return p_st_fluxwkn->u2_fw_status;
    }

    f4_ld = fmaxf(p_st_fluxwkn->p_motor->f4_mtr_ld, FLUXWKN_MIN_PARAM);
    f4_flux = p_st_fluxwkn->p_motor->f4_mtr_m;
    f4_speed_abs = fabsf(f4_speed_rad);

    p_st_fluxwkn->f4_id_demag = -(f4_flux / f4_ld);
    p_st_fluxwkn->f4_id_min = motor_speed_fluxwkn_minf(-p_st_fluxwkn->f4_ia_max, p_st_fluxwkn->f4_id_demag);
    p_st_fluxwkn->f4_v_fw = p_st_fluxwkn->f4_va_max * p_st_fluxwkn->f4_vfw_ratio;

    if ((f4_speed_abs < FLUXWKN_MIN_PARAM) || (p_st_fluxwkn->f4_v_fw <= 0.0f))
    {
        p_st_fluxwkn->u2_fw_status = FLUXWKN_STATE_BYPASSED;
        return p_st_fluxwkn->u2_fw_status;
    }

    f4_flux_limit = p_st_fluxwkn->f4_v_fw / f4_speed_abs;
    f4_id_target = (f4_flux_limit - f4_flux) / f4_ld;

    if (f4_id_target >= 0.0f)
    {
        p_st_fluxwkn->u2_fw_status = FLUXWKN_STATE_BYPASSED;
        return p_st_fluxwkn->u2_fw_status;
    }

    f4_id_target = motor_filter_limitf(f4_id_target, 0.0f, p_st_fluxwkn->f4_id_min);

    if (p_f4_idq_ref[0] > f4_id_target)
    {
        p_f4_idq_ref[0] = f4_id_target;
    }

    f4_iq_max = sqrtf(fmaxf((p_st_fluxwkn->f4_ia_max * p_st_fluxwkn->f4_ia_max) - (p_f4_idq_ref[0] * p_f4_idq_ref[0]), 0.0f));
    f4_iq_sign = (p_f4_idq_ref[1] >= 0.0f) ? 1.0f : -1.0f;
    p_f4_idq_ref[1] = f4_iq_sign * motor_filter_upper_limitf(fabsf(p_f4_idq_ref[1]), f4_iq_max);

    if (p_f4_idq_ref[0] <= (p_st_fluxwkn->f4_id_min + 0.01f))
    {
        p_st_fluxwkn->u2_fw_status = FLUXWKN_STATE_IDSAT;
    }
    else
    {
        p_st_fluxwkn->u2_fw_status = FLUXWKN_STATE_FLUXWKN;
    }

    /* Keep measured value for traceability in debug, even if currently unused by the control law. */
    (void)p_f4_idq[0];
    (void)p_f4_idq[1];

    return p_st_fluxwkn->u2_fw_status;
}

void motor_speed_flux_weakn_motor_set(st_fluxwkn_t * p_st_fluxwkn, const st_motor_parameter_t * p_st_motor)
{
    if (p_st_fluxwkn == 0)
    {
        return;
    }

    p_st_fluxwkn->p_motor = p_st_motor;
}

void motor_speed_flux_weakn_iamax_set(st_fluxwkn_t * p_st_fluxwkn, float f4_ia_max)
{
    if (p_st_fluxwkn == 0)
    {
        return;
    }

    p_st_fluxwkn->f4_ia_max = f4_ia_max;
}

void motor_speed_flux_weakn_vamax_set(st_fluxwkn_t * p_st_fluxwkn, float f4_va_max)
{
    if (p_st_fluxwkn == 0)
    {
        return;
    }

    p_st_fluxwkn->f4_va_max = f4_va_max;
}

void motor_speed_flux_weakn_vfw_ratio_set(st_fluxwkn_t * p_st_fluxwkn, float f4_vfw_ratio)
{
    if (p_st_fluxwkn == 0)
    {
        return;
    }

    p_st_fluxwkn->f4_vfw_ratio = f4_vfw_ratio;
}

float motor_speed_flux_weakn_iamax_get(st_fluxwkn_t * p_st_fluxwkn)
{
    if (p_st_fluxwkn == 0)
    {
        return 0.0f;
    }

    return p_st_fluxwkn->f4_ia_max;
}

float motor_speed_flux_weakn_vamax_get(st_fluxwkn_t * p_st_fluxwkn)
{
    if (p_st_fluxwkn == 0)
    {
        return 0.0f;
    }

    return p_st_fluxwkn->f4_va_max;
}

float motor_speed_flux_weakn_vfw_ratio_get(st_fluxwkn_t * p_st_fluxwkn)
{
    if (p_st_fluxwkn == 0)
    {
        return 0.0f;
    }

    return p_st_fluxwkn->f4_vfw_ratio;
}

uint16_t motor_speed_flux_weakn_status_get(st_fluxwkn_t * p_st_fluxwkn)
{
    if (p_st_fluxwkn == 0)
    {
        return FLUXWKN_STATE_ERROR;
    }

    return p_st_fluxwkn->u2_fw_status;
}

uint8_t motor_speed_flux_weakn_error_check(st_fluxwkn_t * p_st_fluxwkn)
{
    if (p_st_fluxwkn == 0)
    {
        return MTR_TRUE;
    }

    if (motor_speed_flux_weakn_invalid_parameter_check(p_st_fluxwkn) != MTR_FALSE)
    {
        p_st_fluxwkn->u2_fw_status = FLUXWKN_STATE_ERROR;
        return MTR_TRUE;
    }

    if (motor_speed_flux_weakn_run_time_error_check(p_st_fluxwkn) != MTR_FALSE)
    {
        p_st_fluxwkn->u2_fw_status = FLUXWKN_STATE_ERROR;
        return MTR_TRUE;
    }

    return MTR_FALSE;
}

uint8_t motor_speed_flux_weakn_invalid_parameter_check(st_fluxwkn_t * p_st_fluxwkn)
{
    if ((p_st_fluxwkn == 0) || (p_st_fluxwkn->p_motor == 0))
    {
        if (p_st_fluxwkn != 0)
        {
            p_st_fluxwkn->u2_fw_status = FLUXWKN_STATE_INVALID_MOTOR;
        }
        return MTR_TRUE;
    }

    if ((p_st_fluxwkn->p_motor->f4_mtr_ld <= FLUXWKN_MIN_PARAM)
        || (p_st_fluxwkn->p_motor->f4_mtr_lq <= FLUXWKN_MIN_PARAM)
        || (p_st_fluxwkn->p_motor->f4_mtr_m <= FLUXWKN_MIN_PARAM))
    {
        p_st_fluxwkn->u2_fw_status = FLUXWKN_STATE_INVALID_MOTOR;
        return MTR_TRUE;
    }

    if (p_st_fluxwkn->f4_ia_max <= 0.0f)
    {
        p_st_fluxwkn->u2_fw_status = FLUXWKN_STATE_INVALID_IAMAX;
        return MTR_TRUE;
    }

    if (p_st_fluxwkn->f4_va_max <= 0.0f)
    {
        p_st_fluxwkn->u2_fw_status = FLUXWKN_STATE_INVALID_VAMAX;
        return MTR_TRUE;
    }

    if ((p_st_fluxwkn->f4_vfw_ratio < FLUXWKN_DEF_VFWRATIO_MIN) || (p_st_fluxwkn->f4_vfw_ratio > 1.0f))
    {
        p_st_fluxwkn->u2_fw_status = FLUXWKN_STATE_INVALID_VFWRATIO;
        return MTR_TRUE;
    }

    return MTR_FALSE;
}

uint8_t motor_speed_flux_weakn_run_time_error_check(st_fluxwkn_t * p_st_fluxwkn)
{
    if (p_st_fluxwkn == 0)
    {
        return MTR_TRUE;
    }

    if (p_st_fluxwkn->f4_id_min > 0.0f)
    {
        p_st_fluxwkn->u2_fw_status = FLUXWKN_STATE_ERROR;
        return MTR_TRUE;
    }

    return MTR_FALSE;
}

