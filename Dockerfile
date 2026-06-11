FROM golang:1.22-alpine AS builder
WORKDIR /app
COPY go.mod go.sum ./
RUN go mod download
COPY *.go ./
RUN CGO_ENABLED=0 GOOS=linux go build -o comicdownloader .

FROM gcr.io/distroless/static:nonroot
COPY --from=builder /app/comicdownloader /comicdownloader
EXPOSE 8080
ENV PORT=8080
ENTRYPOINT ["/comicdownloader"]
