// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Observability;

namespace Nosum.Host.Ops;

/// <summary>
/// The tiny HTTP listener on its own port (docs/adr/ADR-031 D4, D6): <c>/healthz</c> says the process is up, <c>/readyz</c> says the database answers and
/// every migration is applied, <c>/metrics</c> is the Prometheus scrape. It serves only GET, holds no state and takes no input, and is never published
/// to the internet: the Cloudflare Tunnel reaches only the game WebSocket (docs/adr/ADR-041), and the ops port is not published by compose.
/// </summary>
internal sealed class OpsEndpoint : IAsyncDisposable
{
    /// <summary>Default port of the endpoint; the WebSocket listener is on 57300.</summary>
    public const ushort DefaultPort = 57301;

    private readonly HttpListener _listener = new();
    private readonly Func<CancellationToken, Task<bool>> _ready;
    private readonly PrometheusExporter _metrics;
    private readonly CancellationTokenSource _stop = new();
    private Task _loop = Task.CompletedTask;

    private OpsEndpoint(string host, ushort port, Func<CancellationToken, Task<bool>> ready, PrometheusExporter metrics)
    {
        _ready = ready;
        _metrics = metrics;
        _listener.Prefixes.Add($"http://{host}:{port}/");
    }

    /// <summary>Starts listening.</summary>
    /// <param name="host">The host part of the prefix: <c>localhost</c> for development, <c>+</c> inside a container so the reverse proxy and the compose healthcheck reach it.</param>
    /// <param name="port">The port.</param>
    /// <param name="ready">Answers whether the server is ready for traffic.</param>
    /// <returns>The running endpoint.</returns>
    public static OpsEndpoint Start(string host, ushort port, Func<CancellationToken, Task<bool>> ready)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentNullException.ThrowIfNull(ready);

        PrometheusExporter metrics = new();
        OpsEndpoint endpoint = new(host, port, ready, metrics);
        endpoint._listener.Start();
        endpoint._loop = Task.Run(endpoint.ServeAsync);
        OpsLog.Information("ops.listening", "Health and metrics endpoint is up.", new LogField("host", host), new LogField("port", (int)port));
        return endpoint;
    }

    /// <inheritdoc/>
    [SuppressMessage("Design", "CA1031", Justification = "Shutdown: the loop only reports its own failure.")]
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _listener.Close();
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The listener was closed under the pending accept.
        }

        _metrics.Dispose();
        _stop.Dispose();
    }

    [SuppressMessage("Design", "CA1031", Justification = "One bad request must not stop the endpoint.")]
    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (_stop.IsCancellationRequested || !_listener.IsListening)
            {
                return;
            }
            catch (Exception ex)
            {
                OpsLog.Warning("ops.accept_failed", "The endpoint could not accept a request.", new LogField("error", ex.GetType().Name));
                continue;
            }

            try
            {
                await this.RespondAsync(context).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                OpsLog.Warning("ops.request_failed", "The endpoint failed to answer a request.", new LogField("error", ex.GetType().Name));
            }
        }
    }

    private async Task RespondAsync(HttpListenerContext context)
    {
        HttpListenerResponse response = context.Response;
        string path = context.Request.Url?.AbsolutePath ?? string.Empty;
        int status = 200;
        string contentType = "text/plain; charset=utf-8";
        string body;

        if (context.Request.HttpMethod != "GET" && context.Request.HttpMethod != "HEAD")
        {
            status = 405;
            body = "method not allowed\n";
        }
        else if (path == "/healthz")
        {
            body = "ok\n";
        }
        else if (path == "/readyz")
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            bool ready = await _ready(timeout.Token).ConfigureAwait(false);
            status = ready ? 200 : 503;
            body = ready ? "ready\n" : "not ready\n";
        }
        else if (path == "/metrics")
        {
            contentType = "text/plain; version=0.0.4; charset=utf-8";
            body = _metrics.Render();
        }
        else
        {
            status = 404;
            body = "not found\n";
        }

        byte[] bytes = Encoding.UTF8.GetBytes(body);
        response.StatusCode = status;
        response.ContentType = contentType;
        response.ContentLength64 = bytes.Length;
        if (context.Request.HttpMethod != "HEAD")
        {
            await response.OutputStream.WriteAsync(bytes, _stop.Token).ConfigureAwait(false);
        }

        response.Close();
    }
}
