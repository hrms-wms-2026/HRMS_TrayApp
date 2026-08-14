# Windows Camera Compatibility Gate — Decision Record

**Plan:** Verified Employee Check-In — Plan 1 (Task 0)  
**Date:** 2026-08-13 (updated)  
**Status:** **CONDITIONAL GO — software ready; hardware/AWS liveness matrix pending**

---

## Summary

The disposable spike was folded into the production TrayApp path. The real `@aws-amplify/ui-react-liveness` `FaceLivenessDetectorCore` bundle is now built from `biometric-capture-ui/` and deployed to `ONEVO.Agent.TrayApp/wwwroot/biometric/`. WebView2 virtual-host hosting, camera-permission gating, built-in camera preference logic, and the JS→.NET bridge are implemented and covered by automated tests.

---

## Verified in software (this pass)

| Step | Requirement | Result |
|------|-------------|--------|
| 1 | Packaged `FaceLivenessDetector` build | ✅ `@aws-amplify/ui-react-liveness` production bundle in `wwwroot/biometric/` |
| 2 | WebView2 virtual host (not `file://`) | ✅ `BiometricWebViewHandler` |
| 3 | Camera permission gate (exact origin) | ✅ |
| 4 | Built-in/front camera preference + virtual camera reject | ✅ `bridge.js` `preferBuiltInCameraDeviceId()` |
| 7 | Failure paths don't crash host | ✅ ViewModel + handler tests |
| 9 | No throwaway spike folder | ✅ Integrated into TrayApp |

---

## Pending — requires physical hardware + AWS staging

| Step | Requirement | Blocker |
|------|-------------|---------|
| 5 | ≥480×640, ≥15 FPS negotiated | Needs real `getUserMedia` on target laptops |
| 6 | Real staging liveness session in `ap-south-1` | AWS IAM/KMS not provisioned (Task 1) |
| 7 | Full failure-path matrix on hardware | Teams/Zoom contention, USB webcam, low light |
| 8 | 3–5 laptops, Win10 + Win11 | Physical test fleet |

---

## Machines tested

| Machine | OS | Webcam | Result |
|---------|----|--------|--------|
| **TICS16** (dev) | Windows 11 (10.0.26200) | USB2.0 HD UVC WebCam | ✅ Detected via local probe |
| Fleet matrix (3–5 laptops) | Win10 + Win11 | Various | ☐ Pending |

Probe JSON: `docs/superpowers/plans/2026-08-13-camera-local-probe-result.json`  
Probe script: `scripts/biometrics/probe-local-cameras.ps1`

---

## Decision

**CONDITIONAL GO** for Plan 1 software completion and Plan 2 prep.  
**NO-GO for production user enrollment** until Steps 5–8 pass on real hardware with staging AWS.

---

## Build command (repeatable)

```bash
cd tray_app_maui/biometric-capture-ui
npm run deploy:tray
```

Copies Vite `dist/` into `ONEVO.Agent.TrayApp/wwwroot/biometric/`.
