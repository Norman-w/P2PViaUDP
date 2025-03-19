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

# 检查TURNServer目录是否存在
if [ ! -d "TURNServer" ]; then
    handle_error "找不到TURNServer目录，请确保在正确的位置运行此脚本"
fi

# 1. 拉取最新代码
print_message "${CYAN}" "正在拉取最新代码..."
git pull
check_status "git pull 失败"

# 2. 运行服务
print_message "${GREEN}" "开始运行TURNServer..."
print_message "${CYAN}" "当前工作目录: $(pwd)"
dotnet run --project TURNServer
check_status "dotnet run 失败"