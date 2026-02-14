#!/usr/bin/env python3
"""
MCUScope v2.0 - Launch Script
Starts the FastAPI server and opens the browser.
"""

import argparse
import logging
import os
import sys
import webbrowser
import threading

import uvicorn


def main():
    parser = argparse.ArgumentParser(description='MCUScope v2.0 - MCU Oscilloscope')
    parser.add_argument('--host', default='127.0.0.1', help='Host to bind to')
    parser.add_argument('--port', type=int, default=8080, help='Port to listen on')
    parser.add_argument('--no-browser', action='store_true', help='Do not open browser')
    parser.add_argument('--debug', action='store_true', help='Enable debug mode')
    parser.add_argument('--log-level', default='info',
                        choices=['debug', 'info', 'warning', 'error'])
    args = parser.parse_args()

    logging.basicConfig(
        level=getattr(logging, args.log_level.upper()),
        format='%(asctime)s [%(name)s] %(levelname)s: %(message)s',
        datefmt='%H:%M:%S',
    )

    url = f'http://{args.host}:{args.port}'
    print(f"""
    ╔══════════════════════════════════════╗
    ║         MCUScope v2.0                ║
    ║   MCU Oscilloscope & Analyzer        ║
    ╠══════════════════════════════════════╣
    ║                                      ║
    ║   Server: {url:<24s} ║
    ║                                      ║
    ║   Press Ctrl+C to stop               ║
    ╚══════════════════════════════════════╝
    """)

    if not args.no_browser:
        threading.Timer(1.5, lambda: webbrowser.open(url)).start()

    uvicorn.run(
        'backend.server:app',
        host=args.host,
        port=args.port,
        reload=args.debug,
        log_level=args.log_level,
        access_log=args.debug,
    )


if __name__ == '__main__':
    main()
