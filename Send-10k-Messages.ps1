[CmdletBinding()]
param(
    [ValidateRange(1, 1000000)]
    [int]$MessageCount = 10000,

    [ValidateRange(1, 512)]
    [int]$Concurrency = 100,

    [ValidateRange(1, 600)]
    [int]$RequestTimeoutSeconds = 30,

    [uri]$Endpoint = 'http://localhost:7071/api/products'
)

$ErrorActionPreference = 'Stop'

if ($Endpoint.Scheme -notin @('http', 'https')) {
    throw "Endpoint must use HTTP or HTTPS. Received '$Endpoint'."
}

Add-Type -AssemblyName System.Net.Http

$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.MaxConnectionsPerServer = $Concurrency
$client = [System.Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds($RequestTimeoutSeconds)

$runId = '{0:yyyyMMddHHmmss}-{1}' -f (Get-Date), ([guid]::NewGuid().ToString('N').Substring(0, 8))
$acceptedCount = 0
$failedCount = 0
$failureSamples = [System.Collections.Generic.List[string]]::new()
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

Write-Host "Sending $MessageCount messages to $Endpoint with concurrency $Concurrency..."
Write-Host "Run ID: $runId"

try {
    for ($batchStart = 1; $batchStart -le $MessageCount; $batchStart += $Concurrency) {
        $batchEnd = [Math]::Min($batchStart + $Concurrency - 1, $MessageCount)
        $pendingRequests = [System.Collections.Generic.List[object]]::new()

        for ($sequence = $batchStart; $sequence -le $batchEnd; $sequence++) {
            $sequenceText = '{0:D6}' -f $sequence
            $correlationId = "load-$runId-$sequenceText"
            $payload = [ordered]@{
                productId     = "LOAD-$runId-$sequenceText"
                name          = "Load test product $sequenceText"
                price         = 249.90
                currency      = 'SEK'
                stockQuantity = $sequence % 100
                category      = 'Tools'
            } | ConvertTo-Json -Compress

            $request = [System.Net.Http.HttpRequestMessage]::new(
                [System.Net.Http.HttpMethod]::Post,
                $Endpoint)
            $request.Headers.Add('X-Correlation-Id', $correlationId)
            $request.Content = [System.Net.Http.StringContent]::new(
                $payload,
                [System.Text.Encoding]::UTF8,
                'application/json')

            $pendingRequests.Add([pscustomobject]@{
                Sequence      = $sequence
                CorrelationId = $correlationId
                Request       = $request
                Task          = $client.SendAsync($request)
            })
        }

        foreach ($pendingRequest in $pendingRequests) {
            $response = $null

            try {
                $response = $pendingRequest.Task.GetAwaiter().GetResult()

                if ($response.StatusCode -eq [System.Net.HttpStatusCode]::Accepted) {
                    $acceptedCount++
                }
                else {
                    $failedCount++

                    if ($failureSamples.Count -lt 10) {
                        $responseBody = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                        $failureSamples.Add(
                            "Message $($pendingRequest.Sequence) ($($pendingRequest.CorrelationId)): " +
                            "HTTP $([int]$response.StatusCode) $($response.ReasonPhrase) - $responseBody")
                    }
                }
            }
            catch {
                $failedCount++

                if ($failureSamples.Count -lt 10) {
                    $failureSamples.Add(
                        "Message $($pendingRequest.Sequence) ($($pendingRequest.CorrelationId)): $($_.Exception.Message)")
                }
            }
            finally {
                if ($null -ne $response) {
                    $response.Dispose()
                }

                $pendingRequest.Request.Dispose()
            }
        }

        $completedCount = $acceptedCount + $failedCount
        $percentComplete = [Math]::Floor(($completedCount / $MessageCount) * 100)
        Write-Progress `
            -Activity 'Sending product messages' `
            -Status "$completedCount of $MessageCount complete" `
            -PercentComplete $percentComplete
    }
}
finally {
    $stopwatch.Stop()
    $client.Dispose()
    $handler.Dispose()
    Write-Progress -Activity 'Sending product messages' -Completed
}

$requestsPerSecond = if ($stopwatch.Elapsed.TotalSeconds -gt 0) {
    ($acceptedCount + $failedCount) / $stopwatch.Elapsed.TotalSeconds
}
else {
    0
}

Write-Host ''
Write-Host 'Load run complete.' -ForegroundColor Cyan
Write-Host ('Accepted:            {0:N0}' -f $acceptedCount)
Write-Host ('Failed:              {0:N0}' -f $failedCount)
Write-Host ('Elapsed:             {0:c}' -f $stopwatch.Elapsed)
Write-Host ('Requests per second: {0:N1}' -f $requestsPerSecond)

if ($failureSamples.Count -gt 0) {
    Write-Host ''
    Write-Host 'First failures:' -ForegroundColor Red
    $failureSamples | ForEach-Object { Write-Host "  $_" }
}

if ($failedCount -gt 0) {
    throw "$failedCount of $MessageCount messages were not accepted."
}
