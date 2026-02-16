/***********************************************************************************************************************
* DISCLAIMER
* This software is supplied by Renesas Electronics Corporation and is only intended for use with Renesas products. No
* other uses are authorized. This software is owned by Renesas Electronics Corporation and is protected under all
* applicable laws, including copyright laws.
* THIS SOFTWARE IS PROVIDED "AS IS" AND RENESAS MAKES NO WARRANTIES REGARDING
* THIS SOFTWARE, WHETHER EXPRESS, IMPLIED OR STATUTORY, INCLUDING BUT NOT LIMITED TO WARRANTIES OF MERCHANTABILITY,
* FITNESS FOR A PARTICULAR PURPOSE AND NON-INFRINGEMENT. ALL SUCH WARRANTIES ARE EXPRESSLY DISCLAIMED. TO THE MAXIMUM
* EXTENT PERMITTED NOT PROHIBITED BY LAW, NEITHER RENESAS ELECTRONICS CORPORATION NOR ANY OF ITS AFFILIATED COMPANIES
* SHALL BE LIABLE FOR ANY DIRECT, INDIRECT, SPECIAL, INCIDENTAL OR CONSEQUENTIAL DAMAGES FOR ANY REASON RELATED TO THIS
* SOFTWARE, EVEN IF RENESAS OR ITS AFFILIATES HAVE BEEN ADVISED OF THE POSSIBILITY OF SUCH DAMAGES.
* Renesas reserves the right, without notice, to make changes to this software and to discontinue the availability of
* this software. By using this software, you agree to the additional terms and conditions found by accessing the
* following link:
* http://www.renesas.com/disclaimer
*
* Copyright (C) 2025 Renesas Electronics Corporation. All rights reserved.
***********************************************************************************************************************/
/***********************************************************************************************************************
* File Name   : r_app_mcu.c
* Description : MCU depend application layer functions
***********************************************************************************************************************/
/***********************************************************************************************************************
* History : DD.MM.YYYY Version  Description
*         : 31.01.2025 1.00     First Release
***********************************************************************************************************************/

/***********************************************************************************************************************
* Includes <System Includes> , "Project Includes"
***********************************************************************************************************************/
#include <stdint.h>
#include "r_app_mcu.h"
#include "r_motor_driver_hal.h"
/*** DTC table for ICS ***/
#pragma section DTCTBL
uint32_t g_dtc_table[256];
#pragma section
/*************************/

/***********************************************************************************************************************
* Private functions
***********************************************************************************************************************/
static uint8_t  s_u1_cnt_ics = 0;               /* Counter for period of calling "scope_watchpoint" */
static uint8_t  Poeg_disable = MTR_FALSE;       /* POEG Edge detection flag */

/***********************************************************************************************************************
* Function Name : r_app_hal_init
* Description   : Initialize and Set up peripheral function
* Arguments     : None
* Return Value  : None
***********************************************************************************************************************/
void r_app_hal_init(void)
{
    /* Clear and start hardware OC/short protection */
    R_Config_POEG_Create();/* Clear POE with Create function before starting POE to avoid miss detection */
    R_Config_POEG_Start();

    /* Start motor 3phase timer */
    R_Config_MOTOR_StopTimerCtrl();
    R_Config_MOTOR_StartTimerCount();

    /* Start general purpose timer for speed control interrupt */
    R_Config_CMT0_Start();

    /* Allow A/D converters to be triggered */
    R_Config_S12AD0_Start();
    R_Config_S12AD2_Start();
} /* End of function r_app_hal_init */

/***********************************************************************************************************************
* Function Name : r_app_rmw_hw_init
* Description   : Initialize rmw module
* Arguments     : None
* Return Value  : None
***********************************************************************************************************************/
void r_app_rmw_hw_init(void)
{
    ics2_init((void*)g_dtc_table, ICS_SCI6_P81_P80, ICS_INT_LEVEL, ICS_BRR, ICS_INT_MODE);
} /* End of function r_app_rmw_hw_init */

/***********************************************************************************************************************
* Function Name : r_app_main_loop
* Description   : Main loop of motor control
* Arguments     : None
* Return Value  : None
***********************************************************************************************************************/
void r_app_main_loop(void)
{
    if (0 == POEG.POEGGB.BIT.PIDF)
    {
        if(Poeg_disable == MTR_TRUE)
        {
            R_Config_POEG_Start();
            Poeg_disable = MTR_FALSE;
        }
    }

    /* Restart watchdog timer */
    R_Config_IWDT_Restart();
} /* End of function r_app_main_loop */

/***********************************************************************************************************************
* Function Name : r_app_pwm_highz_reset
* Description   : Reset poeg flag
* Arguments     : None
* Return Value  : None
***********************************************************************************************************************/
void r_app_pwm_highz_reset(void)
{
    Poeg_disable = MTR_TRUE;
    POEG.POEGGB.BIT.PIDF = 0U;
} /* End of function r_app_pwm_highz_reset */

/***********************************************************************************************************************
* Function Name : r_app_rmw_watchpoint
* Description   : Call ICS
* Arguments     : None
* Return Value  : None
***********************************************************************************************************************/
void r_app_rmw_watchpoint(void)
{
    s_u1_cnt_ics++;

    /* Decimation of ICS call */
    if (ICS_DECIMATION < s_u1_cnt_ics)
    {
        s_u1_cnt_ics = 0;

        /* Call ICS */
        ics2_watchpoint();
    }
} /* End of function r_app_rmw_watchpoint */

/***********************************************************************************************************************
* Function Name : ics_rxi_interrupt
* Description   : SCI RXI interrupt for ICS board
* Arguments     : None
* Return Value  : None
***********************************************************************************************************************/
#pragma interrupt ics_rxi_interrupt(vect=VECT(SCI6, RXI6))
static void ics_rxi_interrupt(void)
{
    (void)ics_rxi_interrupt; /* Suppress unused static function warning */
    ics_int_sci_rxi();
} /* End of function ics_rxi_interrupt */

/***********************************************************************************************************************
* Function Name : ics_txi_interrupt
* Description   : SCI TXI interrupt for ICS board
*                 Interrupt triggered by DTC transmission, for preventing undefined interrupt
* Arguments     : None
* Return Value  : None
***********************************************************************************************************************/
#pragma interrupt ics_txi_interrupt(vect=VECT(SCI6, TXI6))
static void ics_txi_interrupt(void)
{
    (void)ics_txi_interrupt; /* Suppress unused static function warning */
    ics_int_sci_txi();
} /* End of function ics_txi_interrupt */

/***********************************************************************************************************************
* Function Name : r_app_board_ui_get_vr1
* Description   : Get A/D converted value of VR1
* Arguments     : None
* Return Value  : A/D converted value of VR1
***********************************************************************************************************************/
uint16_t r_app_board_ui_get_vr1(void)
{
    uint16_t u2_temp0;

    u2_temp0 = (uint16_t)S12AD2.ADDR4;

    return (u2_temp0);
} /* End of function r_app_board_ui_get_vr1 */

/***********************************************************************************************************************
* Function Name : r_app_board_ui_get_sw1
* Description   : Get state of SW1
* Arguments     : None
* Return Value  : State of SW1
***********************************************************************************************************************/
uint8_t r_app_board_ui_get_sw1(void)
{
    uint8_t u1_temp0;

    u1_temp0 = PORT2.PIDR.BIT.B3;

    return (u1_temp0);
} /* End of function r_app_board_ui_get_sw1 */

/***********************************************************************************************************************
* Function Name : r_app_board_ui_get_sw2
* Description   : Get state of SW2
* Arguments     : None
* Return Value  : State of SW2
***********************************************************************************************************************/
uint8_t r_app_board_ui_get_sw2(void)
{
    uint8_t u1_temp0;

    u1_temp0 = PORT2.PIDR.BIT.B2;

    return (u1_temp0);
} /* End of function r_app_board_ui_get_sw2 */

/***********************************************************************************************************************
* Function Name : r_app_board_ui_led1_on
* Description   : Turn on LED1
* Arguments     : None
* Return Value  : None
***********************************************************************************************************************/
void r_app_board_ui_led1_on(void)
{
    PORT2.PODR.BIT.B1 = MTR_LED_ON;
} /* End of function r_app_board_ui_led1_on */

/***********************************************************************************************************************
* Function Name : r_app_board_ui_led2_on
* Description   : Turn on LED2
* Arguments     : None
* Return Value  : None
***********************************************************************************************************************/
void r_app_board_ui_led2_on(void)
{
    PORT2.PODR.BIT.B0 = MTR_LED_ON;
} /* End of function r_app_board_ui_led2_on */

/***********************************************************************************************************************
* Function Name : r_app_board_ui_led1_off
* Description   : Turn off LED1
* Arguments     : None
* Return Value  : None
***********************************************************************************************************************/
void r_app_board_ui_led1_off(void)
{
    PORT2.PODR.BIT.B1 = MTR_LED_OFF;
} /* End of function r_app_board_ui_led1_off */

/***********************************************************************************************************************
* Function Name : r_app_board_ui_led2_off
* Description   : Turn off LED2
* Arguments     : None
* Return Value  : None
***********************************************************************************************************************/
void r_app_board_ui_led2_off(void)
{
    PORT2.PODR.BIT.B0 = MTR_LED_OFF;
} /* End of function r_app_board_ui_led2_off */
