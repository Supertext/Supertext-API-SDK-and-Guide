package com.supertext.sdk;

import java.io.IOException;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/** The default {@link Transport}: the JDK's {@link HttpClient}. */
public final class JdkHttpTransport implements Transport {

    private final HttpClient http;

    /** A transport with its own {@link HttpClient} (follows redirects, 30 s connect timeout). */
    public JdkHttpTransport() {
        this(HttpClient.newBuilder()
                .followRedirects(HttpClient.Redirect.NORMAL)
                .connectTimeout(Duration.ofSeconds(30))
                .build());
    }

    /** A transport on your own {@link HttpClient} (proxy, SSL context, executor, …). */
    public JdkHttpTransport(HttpClient http) {
        this.http = http;
    }

    @Override
    public Response send(Request request) throws IOException, InterruptedException {
        HttpRequest.Builder builder = HttpRequest.newBuilder(request.getUri());
        if (request.getTimeout() != null) {
            builder.timeout(request.getTimeout());
        }
        for (Map.Entry<String, String> header : request.getHeaders().entrySet()) {
            builder.header(header.getKey(), header.getValue());
        }
        byte[] body = request.getBody();
        builder.method(request.getMethod(),
                body == null ? HttpRequest.BodyPublishers.noBody() : HttpRequest.BodyPublishers.ofByteArray(body));

        // The API answers in UTF-8; decode as such whatever the Content-Type says.
        HttpResponse<String> response = http.send(builder.build(), HttpResponse.BodyHandlers.ofString(StandardCharsets.UTF_8));
        Map<String, String> headers = new LinkedHashMap<>();
        for (Map.Entry<String, List<String>> header : response.headers().map().entrySet()) {
            if (!header.getValue().isEmpty()) {
                headers.put(header.getKey(), header.getValue().get(0));
            }
        }
        return new Response(response.statusCode(), headers, response.body());
    }
}
