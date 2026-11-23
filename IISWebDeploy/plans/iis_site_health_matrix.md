# IIS Site Health Matrix & Evaluation Specification

This specification defines the multi-tier health evaluation model for automated scanners monitoring local or remote Internet Information Services (IIS) instances.

---

## 1. Overview & Evaluation Model

A healthy IIS site requires integrity across four distinct functional layers. An automated scanner must evaluate each layer sequentially or in parallel:

```
[ Layer 1: Admin Configuration ]  -> Site & AppPool state in applicationHost.config
           │
[ Layer 2: Runtime & OS ]         -> w3wp.exe execution, rapid-fail, resource limits
           │
[ Layer 3: Transport & TLS ]      -> Port binding, TCP connection, SSL/TLS certificate
           │
[ Layer 4: Application Response ] -> HTTP status codes, TTFB latency, /health endpoints
```

---

## 2. Practical Health Status Matrix

| Health Tier | State | Evaluation Criteria | Operational Meaning | Scanner Action |
| :--- | :--- | :--- | :--- | :--- |
| **Healthy** | `Green` | • Site state is `Started`<br>• App Pool is `Started`<br>• TLS certificate valid for $> 30$ days<br>• Synthetic probe returns `2xx` or expected `3xx`<br>• Latency (TTFB) $< 2.0\text{ s}$<br>• `/health` reports `Healthy` | Site is fully functional and responsive to traffic. | Log normal heartbeat; record telemetry. |
| **Degraded** | `Yellow` | • Site returns `200 OK`, but probe TTFB $\ge 2.0\text{ s}$<br>• TLS certificate expires in $\le 30$ days<br>• Worker process memory or CPU exceeds warning watermark ($> 85\%$) without crashing<br>• `/health` endpoint returns `Degraded` (e.g., non-critical cache miss, read-only replica lag) | Site is serving requests, but failure is imminent or performance is impaired. | Trigger warning notifications; increase polling frequency. |
| **Critical / Down** | `Red` | • Site or App Pool state is `Stopped`<br>• Rapid-fail protection triggered (App Pool faulted)<br>• Port binding conflict or socket connection refused<br>• TLS certificate expired, revoked, or untrusted<br>• Probe returns `5xx` error (`500.19`, `502.5`, `503`, etc.)<br>• Probe times out ($\ge 10\text{ s}$)<br>• `/health` endpoint returns `Unhealthy` | Outage active. Site is unusable by clients. | Raise immediate critical alert; gather crash diagnostics. |

---

## 3. Diagnostic Signatures & Error Taxonomy

When an automated probe detects a `Red` state, map the error signature to the corresponding IIS layer:

### Administrative & Configuration Faults
* **App Pool Stopped (`Rapid-Fail Protection`):**
  * *Cause:* Default threshold reached (typically 5 worker process crashes within 5 minutes).
  * *Detection:* `ApplicationPool.State == ObjectState.Stopped`.
* **Configuration Parsing Failure (`HTTP 500.19`):**
  * *Cause:* Invalid XML in `web.config`, missing IIS native modules (e.g., URL Rewrite, ASP.NET Core Hosting Bundle), or insufficient file read permissions.

### Process & Runtime Faults
* **Process Startup Failure (`HTTP 502.5`):**
  * *Cause:* Out-of-process ASP.NET Core host executable failed to launch or crashed on initialization (e.g., unhandled startup exception, missing runtime framework).
* **Queue Full / Service Unavailable (`HTTP 503`):**
  * *Cause:* App Pool stopped or the kernel-mode `HTTP.sys` queue is saturated beyond capacity (`appConcurrentRequestLimit`).

### Network & Security Faults
* **TLS Certificate Expiration:**
  * *Thresholds:*
    * $> 30$ days remaining: `Healthy`
    * $1\text{ to }30$ days remaining: `Degraded`
    * $\le 0$ days (expired): `Critical`
* **SNI / Host Header Mismatch:**
  * *Cause:* Requests without an appropriate `Host` header hitting an unexpected default site binding.

---

## 4. Recommended Scanner Probing Parameters

| Parameter | Recommended Value | Description |
| :--- | :--- | :--- |
| **Probe Interval** | `30` – `60` seconds | Balances timely detection against resource consumption on the server. |
| **Probe Timeout** | `5.0` – `10.0` seconds | Synthetic requests exceeding this limit should be classified as timed out. |
| **Probe Path** | `/health` $\rightarrow$ `/` | Prioritize dedicated health endpoints; fall back to root path or landing page. |
| **Host Header** | Explicitly set from site binding | Critical for IIS instances hosting multiple applications on shared IP/ports via SNI. |
| **Redirect Handling** | Follow redirects (max 3 hops) | Prevents false positives caused by canonical `301/302` redirects (e.g., HTTP $\rightarrow$ HTTPS). |

---

## 5. Evaluation Logic Pipeline

```csharp
// High-level decision order for the automated probe loop:

if (site.State != ObjectState.Started || appPool.State != ObjectState.Started)
{
    return HealthTier.Critical; // Administrative failure
}

if (certificate.IsExpired)
{
    return HealthTier.Critical; // Security failure
}

var probeResult = await ExecuteSyntheticProbeAsync(endpoint);

if (probeResult.IsTimeout || probeResult.StatusCode >= 500)
{
    return HealthTier.Critical; // Server-side outage
}

if (certificate.DaysUntilExpiration <= 30 || 
    probeResult.ResponseTimeMs > 2000 || 
    probeResult.IsDegradedHealthReport)
{
    return HealthTier.Degraded; // Performance / Maintenance warning
}

return HealthTier.Healthy; // All checks passed
```