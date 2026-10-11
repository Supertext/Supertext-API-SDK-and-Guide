package com.supertext.sdk;

import java.io.IOException;
import java.net.URI;
import java.time.Duration;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.TreeMap;

/**
 * Sends one HTTP request. The default is {@link JdkHttpTransport} ({@code java.net.http.HttpClient});
 * replace it in tests, or to route requests through your platform's HTTP stack.
 *
 * <p>Return every HTTP answer, whatever its status; throw {@link IOException} only when there is no
 * answer (DNS, TLS, connection refused, timeout). The client handles statuses, retries and errors.</p>
 */
@FunctionalInterface
public interface Transport {

    Response send(Request request) throws IOException, InterruptedException;

    /** An HTTP request. */
    final class Request {
        private final String method;
        private final URI uri;
        private final Map<String, String> headers;
        private final byte[] body;
        private final Duration timeout;

        public Request(String method, URI uri, Map<String, String> headers, byte[] body, Duration timeout) {
            this.method = method;
            this.uri = uri;
            this.headers = Collections.unmodifiableMap(new LinkedHashMap<>(headers));
            this.body = body == null ? null : body.clone();
            this.timeout = timeout;
        }

        public String getMethod() {
            return method;
        }

        public URI getUri() {
            return uri;
        }

        /** Request headers in the order they were added (includes {@code Content-Type} when there is a body). */
        public Map<String, String> getHeaders() {
            return headers;
        }

        /** The body, or {@code null} for none. */
        public byte[] getBody() {
            return body == null ? null : body.clone();
        }

        /** How long to wait for the answer. */
        public Duration getTimeout() {
            return timeout;
        }
    }

    /** An HTTP answer. */
    final class Response {
        private final int status;
        private final Map<String, String> headers;
        private final String body;

        /**
         * @param headers first value per header name (looked up case-insensitively)
         * @param body    the body decoded as UTF-8 ({@code ""} for none)
         */
        public Response(int status, Map<String, String> headers, String body) {
            this.status = status;
            TreeMap<String, String> map = new TreeMap<>(String.CASE_INSENSITIVE_ORDER);
            if (headers != null) {
                map.putAll(headers);
            }
            this.headers = Collections.unmodifiableMap(map);
            this.body = body == null ? "" : body;
        }

        public int getStatus() {
            return status;
        }

        /** A header's value, or {@code null}; the name is case-insensitive. */
        public String getHeader(String name) {
            return headers.get(name);
        }

        public String getBody() {
            return body;
        }
    }
}
