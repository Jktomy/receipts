# Atlas receipt adapter: one small dependency-free process in the Receipts stack.
# No OCR engine or independent finance database. Review build before promotion.
FROM node:22.16.0-alpine3.21
WORKDIR /app
COPY tools/atlas/receipt-match.mjs /app/
COPY tools/atlas/receipt-bridge-core.mjs /app/
COPY tools/atlas/receipt-bridge-server.mjs /app/
RUN mkdir -p /data && chown node:node /data
USER node
ENV ATLAS_BRIDGE_BIND=0.0.0.0 \
    ATLAS_BRIDGE_PORT=8675 \
    ATLAS_BRIDGE_DATA_DIR=/data
EXPOSE 8675
CMD ["node", "/app/receipt-bridge-server.mjs"]
