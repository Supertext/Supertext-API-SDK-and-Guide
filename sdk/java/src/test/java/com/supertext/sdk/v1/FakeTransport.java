package com.supertext.sdk.v1;

import com.supertext.sdk.Transport;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Deque;
import java.util.List;
import java.util.Map;
import java.util.function.Function;

/** Records requests and answers them from a queue (or a function). */
final class FakeTransport implements Transport {

    final List<Request> requests = new ArrayList<>();
    private final Deque<Object> answers = new ArrayDeque<>();
    private Function<Request, Response> handler;

    FakeTransport answer(int status, String body) {
        answers.add(new Response(status, Collections.emptyMap(), body));
        return this;
    }

    FakeTransport answer(int status, String body, Map<String, String> headers) {
        answers.add(new Response(status, headers, body));
        return this;
    }

    FakeTransport fail(IOException error) {
        answers.add(error);
        return this;
    }

    FakeTransport handler(Function<Request, Response> handler) {
        this.handler = handler;
        return this;
    }

    @Override
    public Response send(Request request) throws IOException {
        requests.add(request);
        if (handler != null) {
            return handler.apply(request);
        }
        Object next = answers.poll();
        if (next == null) {
            throw new AssertionError("unexpected request " + request.getMethod() + " " + request.getUri());
        }
        if (next instanceof IOException) {
            throw (IOException) next;
        }
        return (Response) next;
    }

    static String body(Request request) {
        byte[] body = request.getBody();
        return body == null ? null : new String(body, StandardCharsets.UTF_8);
    }

    List<String> lines() {
        List<String> out = new ArrayList<>();
        for (Request r : requests) {
            out.add(r.getMethod() + " " + r.getUri());
        }
        return out;
    }
}
