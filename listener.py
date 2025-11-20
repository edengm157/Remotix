import socket
import time

def main():
    # UDP settings
    HOST = '127.0.0.1'  # localhost
    PORT = 12345

    # Create UDP socket
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.bind((HOST, PORT))

    print(f"H.264 Receiver listening on {HOST}:{PORT}")
    print("Waiting for data...\n")

    total_bytes = 0
    packet_count = 0
    start_time = time.time()
    last_print_time = start_time

    try:
        while True:
            # Receive data (max 65507 bytes for UDP)
            data, addr = sock.recvfrom(65507)

            # Update counters
            packet_count += 1
            total_bytes += len(data)

            current_time = time.time()

            # Print stats every second
            if current_time - last_print_time >= 1.0:
                elapsed = current_time - start_time
                mb_received = total_bytes / (1024 * 1024)
                kb_per_sec = (total_bytes / 1024) / elapsed if elapsed > 0 else 0

                print(f"Packets: {packet_count} | "
                      f"Total: {mb_received:.2f} MB | "
                      f"Rate: {kb_per_sec:.2f} KB/s | "
                      f"Last packet: {len(data) / 1024:.2f} KB")

                last_print_time = current_time

    except KeyboardInterrupt:
        print("\n\n=== Final Statistics ===")
        elapsed = time.time() - start_time
        mb_received = total_bytes / (1024 * 1024)
        kb_received = total_bytes / 1024

        print(f"Total packets received: {packet_count}")
        print(f"Total data received: {mb_received:.2f} MB ({kb_received:.2f} KB)")
        print(f"Time elapsed: {elapsed:.2f} seconds")
        print(f"Average rate: {kb_received / elapsed:.2f} KB/s")

    finally:
        sock.close()
        print("\nReceiver closed.")

if __name__ == "__main__":
    main()
