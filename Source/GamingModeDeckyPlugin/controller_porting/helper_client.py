"""Low-rate local control client. Never sends input frames or hardware paths."""
import json
import socket


class HelperClient:
    def __init__(self, port, token, *, timeout=2):
        self.port, self.token, self.timeout = port, token, timeout

    def call(self, command, **options):
        data = json.dumps({"token": self.token, "command": command, "options": options},
                          allow_nan=False).encode() + b"\n"
        if len(data) > 4096:
            raise ValueError("request_too_large")
        with socket.create_connection(("127.0.0.1", self.port), self.timeout) as connection:
            connection.settimeout(self.timeout)
            connection.sendall(data)
            response = connection.makefile("rb").readline(65537)
        if len(response) > 65536 or not response.endswith(b"\n"):
            raise ValueError("invalid_helper_response")
        return json.loads(response)
