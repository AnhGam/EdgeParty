# GIAI ĐOẠN 1: Sử dụng Ubuntu 22.04 làm nền tảng
FROM ubuntu:22.04

# Cấu hình biến môi trường hệ thống
ENV DEBIAN_FRONTEND=noninteractive
ENV TZ=UTC
ENV PORT=7777

# Cài đặt thư viện Unity + gói tíni (Giải quyết vấn đề Signal Handling)
RUN apt-get update && apt-get install -y \
    libglu1 \
    libpulse0 \
    libxcursor1 \
    libxinerama1 \
    libxext6 \
    libx11-6 \
    libxrender1 \
    libasound2 \
    libfontconfig1 \
    libnss3 \
    libnspr4 \
    ca-certificates \
    libatomic1 \
    tini \
    && rm -rf /var/lib/apt/lists/*

# TẠO USER: Bảo mật tối đa (Non-root user)
RUN useradd -ms /bin/bash unity
WORKDIR /app

# COPY BẢN BUILD: Chỉ copy các file cần thiết từ thư mục build của bạn
# (Giả sử file thực thi của bạn là EdgeParty.x86_64)
COPY --chown=unity:unity ./Builds/Linux .

# CẤP QUYỀN: Explicit (Rõ ràng tên file theo review)
# Bạn hãy đổi 'EdgePartyServer.x86_64' nếu tên file thực của bạn khác nhé
RUN chmod +x ./EdgePartyServer.x86_64

# Chuyển sang dùng user unity
USER unity

# Định nghĩa Healthcheck (Kiểm tra xem server còn sống không)
HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=3 \
    CMD pgrep EdgeParty || exit 1

# Mở cổng 7777 UDP
EXPOSE ${PORT}/udp

# CHẠY SERVER QUA TINI (Forward signal chuẩn)
ENTRYPOINT ["/usr/bin/tini", "--", "./EdgePartyServer.x86_64", "-batchmode", "-nographics", "-logFile", "/dev/stdout"]
