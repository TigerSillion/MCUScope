#!/bin/bash
# MCUScope v2.0 - Setup Script
# Installs dependencies and prepares the environment

set -e

echo "=== MCUScope v2.0 Setup ==="
echo ""

# Check Python version
PYTHON_VERSION=$(python3 --version 2>&1 | awk '{print $2}')
echo "Python version: $PYTHON_VERSION"

# Install dependencies
echo ""
echo "Installing Python dependencies..."
pip3 install -r requirements.txt

echo ""
echo "=== Setup Complete ==="
echo ""
echo "To start MCUScope:"
echo "  python3 run.py"
echo ""
echo "Options:"
echo "  --port PORT        Set server port (default: 8080)"
echo "  --no-browser       Don't auto-open browser"
echo "  --debug            Enable debug mode with auto-reload"
echo ""
