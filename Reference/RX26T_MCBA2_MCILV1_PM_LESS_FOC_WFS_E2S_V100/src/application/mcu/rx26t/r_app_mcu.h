/**********************************************************************************************************************
 * DISCLAIMER
 * This software is supplied by Renesas Electronics Corporation and is only intended for use with Renesas products. No
 * other uses are authorized. This software is owned by Renesas Electronics Corporation and is protected under all
 * applicable laws, including copyright laws.
 * THIS SOFTWARE IS PROVIDED "AS IS" AND RENESAS MAKES NO WARRANTIES REGARDING
 * THIS SOFTWARE, WHETHER EXPRESS, IMPLIED OR STATUTORY, INCLUDING BUT NOT LIMITED TO WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NON-INFRINGEMENT. ALL SUCH WARRANTIES ARE EXPRESSLY DISCLAIMED. TO THE MAXIMUM
 * EXTENT PERMITTED NOT PROHIBITED BY LAW, NEITHER RENESAS ELECTRONICS CORPORATION NOR ANY OF ITS AFFILIATED COMPANIES
 * SHALL BE LIABLE FOR ANY DIRECT, INDIRECT, SPECIAL, INCIDENTAL OR CONSEQUENTIAL DAMAGES FOR ANY REASON RELATED TO
 * THIS SOFTWARE, EVEN IF RENESAS OR ITS AFFILIATES HAVE BEEN ADVISED OF THE POSSIBILITY OF SUCH DAMAGES.
 * Renesas reserves the right, without notice, to make changes to this software and to discontinue the availability of
 * this software. By using this software, you agree to the additional terms and conditions found by accessing the
 * following link:
 * http://www.renesas.com/disclaimer
 *
 * Copyright (C) 2025 Renesas Electronics Corporation. All rights reserved.
 *********************************************************************************************************************/
/***********************************************************************************************************************
* File Name   : r_app_mcu.h
* Description : Header for MCU depend application layer functions
***********************************************************************************************************************/
/**********************************************************************************************************************
* History : DD.MM.YYYY Version  Description
*         : 31.01.2025 1.00     First Release
 *********************************************************************************************************************/
#ifndef R_APP_MCU_H
#define R_APP_MCU_H
/**********************************************************************************************************************
 Includes   <System Includes> , "Project Includes"
 *********************************************************************************************************************/
#include "ICS2_RX26T.h"
#include "r_smc_entry.h"

/**********************************************************************************************************************
 Macro definitions
 *********************************************************************************************************************/
#define     ICS_DECIMATION               (3)                  /* ICS watch skipping number */

/* For ICS */
#define     ICS_INT_LEVEL                (4)                  /* SCI6 RX/TX interrupt level */
#define     ICS_BRR                      (4)                  /* PCLKB/(8*(BRR+1)) => ~1Mbps at PCLKB=40MHz */
#define     ICS_INT_MODE                 (1)                  /* Mode select */
#define     CONF_MOTOR_TYPE              ("Brushless DC Motor")
#define     CONF_CONTROL                 ("Sensorless vector control (Speed control)")
#define     CONF_INVERTER                ("MCI-LV-1")
#define     CONF_MOTOR_TYPE_LEN          (18)
#define     CONF_CONTROL_LEN             (41)
#define     CONF_INVERTER_LEN            (8)

/* Defines the UI used as default UI (MAIN_UI_BOARD/MAIN_UI_RMW)*/
#define     APP_CFG_USE_UI               (MAIN_UI_RMW)

/* design parameter */
#define     APP_CFG_FREQ_BAND_LIMIT      (3.0f)               /* Motor control natural frequency limit */
#define     APP_CFG_MAX_CURRENT_OMEGA    (1000.0f)            /* Max natural frequency of current loop [Hz] */
#define     APP_CFG_MIN_OMEGA            (1.0f)               /* Min natural frequency of control loop [Hz] */

/**********************************************************************************************************************
 Global Typedef definitions
 *********************************************************************************************************************/

/**********************************************************************************************************************
 External global variables
 *********************************************************************************************************************/

/**********************************************************************************************************************
 Exported global functions
 *********************************************************************************************************************/
void r_app_rmw_hw_init(void);
void r_app_hal_init(void);
void r_app_main_loop(void);
void r_app_pwm_highz_reset(void);
void r_app_rmw_watchpoint(void);
uint16_t r_app_board_ui_get_vr1(void);
uint8_t r_app_board_ui_get_sw1(void);
uint8_t r_app_board_ui_get_sw2(void);
void r_app_board_ui_led1_on(void);
void r_app_board_ui_led2_on(void);
void r_app_board_ui_led1_off(void);
void r_app_board_ui_led2_off(void);

#endif /* R_APP_MCU_H */
