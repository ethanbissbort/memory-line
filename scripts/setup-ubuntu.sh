#!/bin/bash

#
# Verify and install dependencies for Memory Timeline Application on Ubuntu 24.x
#
# This script checks for and installs all required dependencies for both
# development and production environments on Ubuntu 24.04 LTS.
#
# Usage:
#   ./setup-ubuntu.sh [dev|prod] [--auto]
#
# Arguments:
#   dev|prod  - Mode: 'dev' for development (default), 'prod' for production only
#   --auto    - Automatically install missing dependencies without prompting
#
# Examples:
#   ./setup-ubuntu.sh dev --auto
#   ./setup-ubuntu.sh prod
#

set -e  # Exit on error

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Default values
MODE="${1:-dev}"
AUTO_INSTALL=false

# Parse arguments
for arg in "$@"; do
    case $arg in
        --auto)
            AUTO_INSTALL=true
            shift
            ;;
        dev|prod)
            MODE="$arg"
            shift
            ;;
    esac
done

# Track installation status
INSTALLATION_NEEDED=false
INSTALLATION_FAILED=false

echo -e "${CYAN}========================================"
echo "Memory Timeline - Dependency Setup"
echo "Ubuntu 24.x Setup Script"
echo "Mode: $MODE"
echo -e "========================================${NC}\n"

# Function to check if command exists
command_exists() {
    command -v "$1" >/dev/null 2>&1
}

# Function to get version
get_version() {
    local cmd="$1"
    local flag="${2:---version}"
    $cmd $flag 2>&1 | head -n1 | grep -oP '\d+\.\d+(\.\d+)?' | head -n1
}

# Function to compare versions (returns 0 if $1 >= $2)
version_gte() {
    [ "$(printf '%s\n' "$1" "$2" | sort -V | head -n1)" = "$2" ]
}

# Function to install package
install_package() {
    local package="$1"
    local name="$2"

    echo -e "${YELLOW}Installing $name...${NC}"
    if sudo apt-get install -y "$package"; then
        echo -e "${GREEN}✓ $name installed successfully${NC}"
        return 0
    else
        echo -e "${RED}✗ Failed to install $name${NC}"
        INSTALLATION_FAILED=true
        return 1
    fi
}

# Function to prompt for installation
prompt_install() {
    local name="$1"

    if [ "$AUTO_INSTALL" = true ]; then
        return 0
    else
        read -p "Install $name? (Y/n) " -n 1 -r
        echo
        if [[ ! $REPLY =~ ^[Nn]$ ]]; then
            return 0
        else
            return 1
        fi
    fi
}

# Check if running on Ubuntu
if [ -f /etc/os-release ]; then
    . /etc/os-release
    if [[ "$ID" != "ubuntu" ]]; then
        echo -e "${YELLOW}⚠ Warning: This script is designed for Ubuntu 24.x${NC}"
        echo -e "${YELLOW}  Your OS: $PRETTY_NAME${NC}"
        echo -e "${YELLOW}  Continuing anyway...${NC}\n"
    fi
else
    echo -e "${YELLOW}⚠ Warning: Cannot detect OS version${NC}\n"
fi

# ============================================
# Check 0: Update package list
# ============================================
echo -e "${CYAN}[0/6] Updating package list...${NC}"
if sudo apt-get update -qq; then
    echo -e "${GREEN}✓ Package list updated${NC}"
else
    echo -e "${YELLOW}⚠ Failed to update package list${NC}"
fi

# ============================================
# Check 1: Node.js
# ============================================
echo -e "\n${CYAN}[1/6] Checking Node.js...${NC}"

if command_exists node; then
    NODE_VERSION=$(get_version node -v)
    NODE_VERSION_CLEAN=$(echo "$NODE_VERSION" | sed 's/^v//')
    echo -e "${GREEN}✓ Node.js v$NODE_VERSION_CLEAN found${NC}"

    # Check if version meets requirement (16+)
    if ! version_gte "$NODE_VERSION_CLEAN" "16.0.0"; then
        echo -e "${YELLOW}⚠ Node.js version $NODE_VERSION_CLEAN is below recommended 16.0.0${NC}"
        echo -e "${YELLOW}  Consider updating Node.js${NC}"
    fi
else
    echo -e "${RED}✗ Node.js not found${NC}"
    INSTALLATION_NEEDED=true

    if prompt_install "Node.js LTS"; then
        echo -e "${YELLOW}Installing Node.js via NodeSource repository...${NC}"

        # Install Node.js 20.x LTS from NodeSource
        curl -fsSL https://deb.nodesource.com/setup_20.x | sudo -E bash -
        if install_package "nodejs" "Node.js"; then
            NODE_VERSION=$(get_version node -v)
            echo -e "${GREEN}✓ Node.js $NODE_VERSION installed${NC}"
        fi
    else
        echo -e "${YELLOW}Skipping Node.js installation${NC}"
        INSTALLATION_FAILED=true
    fi
fi

# ============================================
# Check 2: npm
# ============================================
echo -e "\n${CYAN}[2/6] Checking npm...${NC}"

if command_exists npm; then
    NPM_VERSION=$(get_version npm)
    echo -e "${GREEN}✓ npm $NPM_VERSION found${NC}"
else
    echo -e "${RED}✗ npm not found${NC}"
    echo -e "${YELLOW}  npm is typically installed with Node.js${NC}"

    # Try to install npm separately
    if prompt_install "npm"; then
        install_package "npm" "npm"
    else
        INSTALLATION_FAILED=true
    fi
fi

# ============================================
# Check 3: Python
# ============================================
echo -e "\n${CYAN}[3/6] Checking Python...${NC}"

PYTHON_INSTALLED=false
for cmd in python3 python; do
    if command_exists "$cmd"; then
        PYTHON_VERSION=$(get_version "$cmd" --version)
        echo -e "${GREEN}✓ Python $PYTHON_VERSION found ($cmd)${NC}"
        PYTHON_INSTALLED=true
        break
    fi
done

if [ "$PYTHON_INSTALLED" = false ]; then
    echo -e "${RED}✗ Python not found${NC}"
    echo -e "${YELLOW}  Python is required for building native Node.js modules (better-sqlite3)${NC}"
    INSTALLATION_NEEDED=true

    if prompt_install "Python 3"; then
        install_package "python3" "Python 3"
    else
        INSTALLATION_FAILED=true
    fi
fi

# ============================================
# Check 4: Build essentials
# ============================================
echo -e "\n${CYAN}[4/6] Checking build tools...${NC}"

BUILD_TOOLS_INSTALLED=true
for tool in gcc g++ make; do
    if ! command_exists "$tool"; then
        BUILD_TOOLS_INSTALLED=false
        break
    fi
done

if [ "$BUILD_TOOLS_INSTALLED" = true ]; then
    GCC_VERSION=$(get_version gcc)
    echo -e "${GREEN}✓ Build tools found (gcc $GCC_VERSION)${NC}"
else
    echo -e "${RED}✗ Build tools not found${NC}"
    echo -e "${YELLOW}  Required for compiling native Node.js modules (better-sqlite3)${NC}"
    INSTALLATION_NEEDED=true

    if prompt_install "build-essential"; then
        install_package "build-essential" "Build Essential (gcc, g++, make)"
    else
        INSTALLATION_FAILED=true
    fi
fi

# ============================================
# Check 5: Additional build dependencies
# ============================================
echo -e "\n${CYAN}[5/6] Checking additional dependencies...${NC}"

DEPS_NEEDED=()
for dep in pkg-config libsqlite3-dev; do
    if ! dpkg -s "$dep" >/dev/null 2>&1; then
        DEPS_NEEDED+=("$dep")
    fi
done

if [ ${#DEPS_NEEDED[@]} -eq 0 ]; then
    echo -e "${GREEN}✓ All additional dependencies found${NC}"
else
    echo -e "${YELLOW}⚠ Missing dependencies: ${DEPS_NEEDED[*]}${NC}"
    INSTALLATION_NEEDED=true

    if prompt_install "additional dependencies"; then
        for dep in "${DEPS_NEEDED[@]}"; do
            install_package "$dep" "$dep"
        done
    else
        echo -e "${YELLOW}Skipping optional dependencies (may cause build issues)${NC}"
    fi
fi

# ============================================
# Check 6: Git (optional but recommended)
# ============================================
echo -e "\n${CYAN}[6/6] Checking Git...${NC}"

if command_exists git; then
    GIT_VERSION=$(get_version git)
    echo -e "${GREEN}✓ Git $GIT_VERSION found${NC}"
else
    echo -e "${YELLOW}⚠ Git not found (optional but recommended)${NC}"

    if prompt_install "Git"; then
        install_package "git" "Git"
    fi
fi

# ============================================
# Install npm dependencies
# ============================================
echo -e "\n${CYAN}========================================"
echo "Installing npm dependencies..."
echo -e "========================================${NC}\n"

if command_exists npm && [ -f "package.json" ]; then
    echo -e "${YELLOW}Running npm install...${NC}"

    # Set npm config for better compilation
    npm config set python python3

    if [ "$MODE" = "prod" ]; then
        if npm install --production --no-optional; then
            echo -e "\n${GREEN}✓ npm dependencies installed successfully${NC}"
        else
            echo -e "\n${RED}✗ npm install failed${NC}"
            echo -e "${YELLOW}  If you're behind a proxy or have network issues, try:${NC}"
            echo -e "${YELLOW}  npm install --ignore-scripts${NC}"
            INSTALLATION_FAILED=true
        fi
    else
        if npm install; then
            echo -e "\n${GREEN}✓ npm dependencies installed successfully${NC}"
        else
            echo -e "\n${RED}✗ npm install failed${NC}"
            echo -e "${YELLOW}  Try running with verbose output:${NC}"
            echo -e "${YELLOW}  npm install --verbose${NC}"
            INSTALLATION_FAILED=true
        fi
    fi
elif [ ! -f "package.json" ]; then
    echo -e "${YELLOW}⚠ package.json not found in current directory${NC}"
    echo -e "${YELLOW}  Please run this script from the project root directory${NC}"
    INSTALLATION_FAILED=true
fi

# ============================================
# Summary
# ============================================
echo -e "\n${CYAN}========================================"
echo "Setup Summary"
echo -e "========================================${NC}\n"

if [ "$INSTALLATION_FAILED" = true ]; then
    echo -e "${YELLOW}⚠ Setup completed with errors${NC}"
    echo -e "${YELLOW}Please review the messages above and install missing dependencies manually.${NC}"
    echo -e "\n${YELLOW}Common issues:${NC}"
    echo -e "${YELLOW}  1. Run 'sudo apt-get update' if packages cannot be found${NC}"
    echo -e "${YELLOW}  2. Ensure you have internet connectivity${NC}"
    echo -e "${YELLOW}  3. Check if /etc/apt/sources.list is properly configured${NC}"
    exit 1
elif [ "$INSTALLATION_NEEDED" = true ]; then
    echo -e "${CYAN}ℹ Some dependencies were installed${NC}"
    echo -e "${CYAN}All dependencies should now be available.${NC}"
    echo -e "\n${CYAN}Next steps:${NC}"
    echo -e "${CYAN}  1. Run 'npm run dev' to start development mode${NC}"
    echo -e "${CYAN}  2. Run 'npm test' to verify the setup${NC}"
    exit 0
else
    echo -e "${GREEN}✓ All dependencies are installed!${NC}"
    echo -e "\n${GREEN}You're ready to start developing!${NC}"
    echo -e "\n${CYAN}Next steps:${NC}"
    echo -e "${CYAN}  Development: npm run dev${NC}"
    echo -e "${CYAN}  Build:       npm run build${NC}"
    echo -e "${CYAN}  Package:     npm run package${NC}"
    echo -e "${CYAN}  Test:        npm test${NC}"
    exit 0
fi
