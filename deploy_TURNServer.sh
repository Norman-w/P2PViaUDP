#!/bin/bash

# 定义颜色代码
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# 输出带颜色的信息函数
print_message() {
    color=$1
    message=$2
    echo -e "${color}${message}${NC}"
}

# 错误处理函数
handle_error() {
    print_message "${RED}" "错误: $1"
    exit 1
}

# 检查上一个命令是否成功
check_status() {
    if [ $? -ne 0 ]; then
        handle_error "$1"
    fi
}

# 检查是否在TURNServer目录
if [ ! -f "Program.cs" ]; then
    print_message "${YELLOW}" "未在TURNServer目录中，尝试切换到正确目录..."
    # 切换到上级目录
    cd ..
    if [ ! -d "TURNServer" ]; then
        handle_error "找不到TURNServer目录，请确保在正确的位置运行此脚本"
    fi
fi

# 1. 拉取最新代码
print_message "${CYAN}" "正在拉取最新代码..."
git pull
check_status "git pull 失败"

# 2. 切换到TURNServer目录
print_message "${CYAN}" "切换到TURNServer目录..."
cd TURNServer
check_status "切换目录失败"

# 3. 运行服务
print_message "${GREEN}" "开始运行TURNServer..."
print_message "${CYAN}" "当前工作目录: $(pwd)"
dotnet run
check_status "dotnet run 失败"