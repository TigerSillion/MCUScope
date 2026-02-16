/***********************************************************************************************************************
* File Name   : r_motor_current_volt_err_comp.c
* Description : Open C implementation of inverter voltage error compensation.
***********************************************************************************************************************/

#include <stdint.h>
#include <math.h>
#include <string.h>

#include "r_motor_current_volt_err_comp.h"
#include "r_motor_filter.h"

#define VERR_MIN_DELTA                (1.0e-6f)

static float motor_current_verr_abs(float value)
{
    return (value >= 0.0f) ? value : -value;
}

static uint8_t motor_current_verr_table_index(const st_volt_comp_t *p_st_volt_comp, float f4_abs_current)
{
    uint8_t u1_idx;

    if (f4_abs_current <= p_st_volt_comp->f4_comp_i[0])
    {
        return 0U;
    }

    for (u1_idx = 0U; u1_idx < (VERR_COMP_ARY_SIZE - 1U); u1_idx++)
    {
        if (f4_abs_current <= p_st_volt_comp->f4_comp_i[u1_idx + 1U])
        {
            return u1_idx;
        }
    }

    return (VERR_COMP_ARY_SIZE - 2U);
}

static float motor_current_verr_interp(const st_volt_comp_t *p_st_volt_comp, float f4_abs_current)
{
    uint8_t u1_idx;
    float f4_comp;

    if (f4_abs_current <= p_st_volt_comp->f4_comp_i[0])
    {
        return p_st_volt_comp->f4_comp_v[0];
    }

    if (f4_abs_current >= p_st_volt_comp->f4_comp_i[VERR_COMP_ARY_SIZE - 1U])
    {
        return p_st_volt_comp->f4_comp_v[VERR_COMP_ARY_SIZE - 1U];
    }

    u1_idx = motor_current_verr_table_index(p_st_volt_comp, f4_abs_current);
    f4_comp = (p_st_volt_comp->f4_slope[u1_idx] * f4_abs_current) + p_st_volt_comp->f4_intcept[u1_idx];

    return f4_comp;
}

void motor_current_volt_err_comp_init(st_volt_comp_t *p_st_volt_comp, uint8_t u1_volt_comp_use_motor_type)
{
    static const float f4_default_i[VERR_COMP_ARY_SIZE] =
    {
        VERR_COMP_TBL_COMP_I0,
        VERR_COMP_TBL_COMP_I1,
        VERR_COMP_TBL_COMP_I2,
        VERR_COMP_TBL_COMP_I3,
        VERR_COMP_TBL_COMP_I4
    };
    static const float f4_default_v[VERR_COMP_ARY_SIZE] =
    {
        VERR_COMP_TBL_COMP_V0,
        VERR_COMP_TBL_COMP_V1,
        VERR_COMP_TBL_COMP_V2,
        VERR_COMP_TBL_COMP_V3,
        VERR_COMP_TBL_COMP_V4
    };

    if (p_st_volt_comp == 0)
    {
        return;
    }

    memset(p_st_volt_comp, 0, sizeof(st_volt_comp_t));
    p_st_volt_comp->u1_volt_err_comp_enable = VERR_COMP_ENABLE;
    p_st_volt_comp->u1_volt_comp_use_motor_type = u1_volt_comp_use_motor_type;
    p_st_volt_comp->f4_volt_comp_limit_ratio = 0.10f;
    p_st_volt_comp->f4_vdc = VERR_COMP_REF_VOLTAGE;

    motor_current_volt_err_comp_table_set(p_st_volt_comp, f4_default_i, f4_default_v, VERR_COMP_REF_VOLTAGE);
    motor_current_volt_err_comp_reset(p_st_volt_comp);
}

void motor_current_volt_err_comp_reset(st_volt_comp_t *p_st_volt_comp)
{
    if (p_st_volt_comp == 0)
    {
        return;
    }

    p_st_volt_comp->f4_volt_comp_array[0] = 0.0f;
    p_st_volt_comp->f4_volt_comp_array[1] = 0.0f;
    p_st_volt_comp->f4_volt_comp_array[2] = 0.0f;
}

void motor_current_volt_err_comp_main(st_volt_comp_t *p_st_volt_comp,
                                      float *p_f4_v_array,
                                      float *p_f4_i_array,
                                      float f4_vdc)
{
    float f4_vdc_ratio;
    float f4_ref_vdc;
    float f4_limit;
    uint8_t u1_idx;

    if ((p_st_volt_comp == 0) || (p_f4_v_array == 0) || (p_f4_i_array == 0))
    {
        return;
    }

    if ((p_st_volt_comp->u1_volt_err_comp_enable == VERR_COMP_DISABLE)
        || (p_st_volt_comp->u1_volt_comp_use_motor_type == VERROR_COMP_USE_OFF))
    {
        return;
    }

    f4_ref_vdc = fmaxf(p_st_volt_comp->f4_vdc, VERR_MIN_DELTA);
    f4_vdc_ratio = fmaxf(f4_vdc, VERR_MIN_DELTA) / f4_ref_vdc;
    f4_limit = fmaxf(f4_vdc, VERR_MIN_DELTA) * p_st_volt_comp->f4_volt_comp_limit_ratio;

    p_st_volt_comp->f4_vdc = f4_vdc;
    p_st_volt_comp->f4_volt_comp_limit = f4_limit;

    for (u1_idx = 0U; u1_idx < 3U; u1_idx++)
    {
        float f4_abs_i = motor_current_verr_abs(p_f4_i_array[u1_idx]);
        float f4_comp = motor_current_verr_interp(p_st_volt_comp, f4_abs_i) * f4_vdc_ratio;

        if (p_f4_i_array[u1_idx] < 0.0f)
        {
            f4_comp = -f4_comp;
        }

        p_st_volt_comp->f4_volt_comp_array[u1_idx] = motor_filter_limitf_abs(f4_comp, f4_limit);
    }

    if (p_st_volt_comp->u1_volt_comp_use_motor_type == VERROR_COMP_USE_AB)
    {
        p_f4_v_array[0] += p_st_volt_comp->f4_volt_comp_array[0];
        p_f4_v_array[1] += p_st_volt_comp->f4_volt_comp_array[1];
    }
    else
    {
        p_f4_v_array[0] += p_st_volt_comp->f4_volt_comp_array[0];
        p_f4_v_array[1] += p_st_volt_comp->f4_volt_comp_array[1];
        p_f4_v_array[2] += p_st_volt_comp->f4_volt_comp_array[2];
    }
}

void motor_current_volt_err_comp_table_set(st_volt_comp_t *p_st_volt_comp,
                                       const float *f4_current_table,
                                       const float *f4_volterr_table,
                                       float f4_ref_vdc)
{
    uint8_t u1_idx;

    if ((p_st_volt_comp == 0) || (f4_current_table == 0) || (f4_volterr_table == 0))
    {
        return;
    }

    for (u1_idx = 0U; u1_idx < VERR_COMP_ARY_SIZE; u1_idx++)
    {
        p_st_volt_comp->f4_comp_i[u1_idx] = f4_current_table[u1_idx];
        p_st_volt_comp->f4_comp_v[u1_idx] = f4_volterr_table[u1_idx];
    }

    for (u1_idx = 0U; u1_idx < (VERR_COMP_ARY_SIZE - 1U); u1_idx++)
    {
        float f4_dx = p_st_volt_comp->f4_comp_i[u1_idx + 1U] - p_st_volt_comp->f4_comp_i[u1_idx];

        if (motor_current_verr_abs(f4_dx) < VERR_MIN_DELTA)
        {
            p_st_volt_comp->f4_slope[u1_idx] = 0.0f;
        }
        else
        {
            p_st_volt_comp->f4_slope[u1_idx] =
                (p_st_volt_comp->f4_comp_v[u1_idx + 1U] - p_st_volt_comp->f4_comp_v[u1_idx]) / f4_dx;
        }

        p_st_volt_comp->f4_intcept[u1_idx] =
            p_st_volt_comp->f4_comp_v[u1_idx] - (p_st_volt_comp->f4_slope[u1_idx] * p_st_volt_comp->f4_comp_i[u1_idx]);
    }

    p_st_volt_comp->f4_slope[VERR_COMP_ARY_SIZE - 1U] = p_st_volt_comp->f4_slope[VERR_COMP_ARY_SIZE - 2U];
    p_st_volt_comp->f4_intcept[VERR_COMP_ARY_SIZE - 1U] = p_st_volt_comp->f4_intcept[VERR_COMP_ARY_SIZE - 2U];
    p_st_volt_comp->f4_vdc = fmaxf(f4_ref_vdc, VERR_MIN_DELTA);
}

void motor_current_volt_err_vlimit_set(st_volt_comp_t *p_st_volt_comp, float f4_vlimit_ratio)
{
    if (p_st_volt_comp == 0)
    {
        return;
    }

    p_st_volt_comp->f4_volt_comp_limit_ratio = motor_filter_limitf(f4_vlimit_ratio, 1.0f, 0.0f);
}
