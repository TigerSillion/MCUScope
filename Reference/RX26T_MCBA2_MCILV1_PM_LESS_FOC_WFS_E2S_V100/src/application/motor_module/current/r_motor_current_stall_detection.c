/***********************************************************************************************************************
* File Name   : r_motor_current_stall_detection.c
* Description : Open C implementation of stall detection helper.
***********************************************************************************************************************/

#include <stdint.h>
#include <math.h>

#include "r_motor_current_stall_detection.h"
#include "r_motor_filter.h"

static float motor_stall_det_clamp01(float value)
{
    if (value < 0.0f)
    {
        return 0.0f;
    }

    if (value > 1.0f)
    {
        return 1.0f;
    }

    return value;
}

void motor_current_stall_detection_init(st_stall_detect_t *p_st_stall_det,
                                        const st_stall_detect_cfg_t *p_st_stall_det_cfg)
{
    if ((p_st_stall_det == 0) || (p_st_stall_det_cfg == 0))
    {
        return;
    }

    motor_current_stall_detection_parameter_set(p_st_stall_det, p_st_stall_det_cfg);
    motor_current_stall_detection_reset(p_st_stall_det);
}

void motor_current_stall_detection_parameter_set(st_stall_detect_t *p_st_stall_det,
                                                 const st_stall_detect_cfg_t *p_st_stall_det_cfg)
{
    if ((p_st_stall_det == 0) || (p_st_stall_det_cfg == 0))
    {
        return;
    }

    p_st_stall_det->f4_threshold_level = fmaxf(p_st_stall_det_cfg->f4_threshold_level, 0.0f);
    p_st_stall_det->f4_id_hpf_time = motor_stall_det_clamp01(p_st_stall_det_cfg->f4_id_hpf_time);
    p_st_stall_det->f4_iq_hpf_time = motor_stall_det_clamp01(p_st_stall_det_cfg->f4_iq_hpf_time);
    p_st_stall_det->f4_threshold_time = motor_stall_det_clamp01(p_st_stall_det_cfg->f4_threshold_time);
}

void motor_current_stall_detection_reset(st_stall_detect_t *p_st_stall_det)
{
    if (p_st_stall_det == 0)
    {
        return;
    }

    p_st_stall_det->f4_id_current_abs_val = 0.0f;
    p_st_stall_det->f4_iq_current_abs_val = 0.0f;
    p_st_stall_det->u1_stall_detected = 0U;
}

void motor_current_stall_detection_main(st_stall_detect_t *p_st_stall_det, float *p_f4_i_array)
{
    float f4_id_abs;
    float f4_iq_abs;
    float f4_amp_abs;
    float f4_thr_eval;

    if ((p_st_stall_det == 0) || (p_f4_i_array == 0))
    {
        return;
    }

    f4_id_abs = fabsf(p_f4_i_array[0]);
    f4_iq_abs = fabsf(p_f4_i_array[1]);

    p_st_stall_det->f4_id_current_abs_val = motor_filter_lpff(f4_id_abs,
                                                              p_st_stall_det->f4_id_current_abs_val,
                                                              p_st_stall_det->f4_id_hpf_time);
    p_st_stall_det->f4_iq_current_abs_val = motor_filter_lpff(f4_iq_abs,
                                                              p_st_stall_det->f4_iq_current_abs_val,
                                                              p_st_stall_det->f4_iq_hpf_time);

    f4_amp_abs = sqrtf((p_st_stall_det->f4_id_current_abs_val * p_st_stall_det->f4_id_current_abs_val)
                     + (p_st_stall_det->f4_iq_current_abs_val * p_st_stall_det->f4_iq_current_abs_val));

    /* Use an additional soft filter to avoid one-shot stall spikes. */
    f4_thr_eval = motor_filter_lpff(f4_amp_abs,
                                    0.5f * (p_st_stall_det->f4_id_current_abs_val + p_st_stall_det->f4_iq_current_abs_val),
                                    p_st_stall_det->f4_threshold_time);

    if (f4_thr_eval >= p_st_stall_det->f4_threshold_level)
    {
        p_st_stall_det->u1_stall_detected = 1U;
    }
    else
    {
        p_st_stall_det->u1_stall_detected = 0U;
    }
}
