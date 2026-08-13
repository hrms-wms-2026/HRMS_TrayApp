// PLACEHOLDER SCAFFOLD — not the production capture surface.
//
// This file stands in for the packaged React app (@aws-amplify/ui-react-liveness's
// <FaceLivenessDetector>) that Plan 1 Task 0 (Windows Camera Compatibility Gate) was
// supposed to validate and hand off here. Task 0 was intentionally skipped in this pass
// (no AWS access / physical Windows hardware available) — see
// docs/superpowers/plans/2026-08-13-verified-checkin-foundation.md, Task 0 and Task 20 Step 7.
//
// What IS real and load-bearing here (do not remove when replacing this file):
//   1. getSessionConfig() — reads window.__onevoLivenessConfig, injected by
//      BiometricWebViewHandler.PushSessionConfigAsync via ExecuteScriptAsync. Shape:
//      { AwsSessionId, Region, ChallengeType, AccessKeyId, SecretAccessKey, SessionToken }
//   2. reportCaptureFinished(succeeded, errorCode) — the ONLY channel back to .NET.
//      BiometricWebViewHandler listens on CoreWebView2.WebMessageReceived and deserializes
//      the JSON into BiometricCaptureOutcome(bool Succeeded, string? ErrorCode).
//
// Before this can run a real liveness session:
//   - Run Task 0's compatibility gate on real target hardware with real AWS ap-south-1
//     credentials to confirm WebView2 + getUserMedia + StartFaceLivenessSession actually work
//     on the fleet's laptops.
//   - Replace this file's <div id="root"> content with the real
//     @aws-amplify/ui-react-liveness FaceLivenessDetector component, wired to call
//     getSessionConfig() for sessionId/region/credentials and reportCaptureFinished(...)
//     on its onAnalysisComplete / onError callbacks.

function getSessionConfig() {
  return window.__onevoLivenessConfig || null;
}

function reportCaptureFinished(succeeded, errorCode) {
  window.chrome.webview.postMessage(JSON.stringify({ Succeeded: succeeded, ErrorCode: errorCode }));
}

// Scaffold-only visual: proves the bridge wiring end-to-end without a real capture.
// DELETE this block once the real FaceLivenessDetector component is wired in.
window.addEventListener("load", () => {
  const check = setInterval(() => {
    const config = getSessionConfig();
    if (config) {
      clearInterval(check);
      document.getElementById("root").textContent =
        "Capture session ready (scaffold — no real liveness capture wired yet): " + config.AwsSessionId;
    }
  }, 250);
});
