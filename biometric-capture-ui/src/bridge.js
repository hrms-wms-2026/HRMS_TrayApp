/** @returns {import('./types').OnevoLivenessConfig | null} */
export function getSessionConfig() {
  return window.__onevoLivenessConfig ?? null;
}

/** @param {boolean} succeeded @param {string | null | undefined} errorCode */
export function reportCaptureFinished(succeeded, errorCode) {
  if (!window.chrome?.webview?.postMessage) {
    console.error('WebView2 bridge unavailable');
    return;
  }

  window.chrome.webview.postMessage(
    JSON.stringify({ Succeeded: succeeded, ErrorCode: errorCode ?? null }),
  );
}

const VIRTUAL_CAMERA_LABELS = [
  'obs virtual',
  'snap camera',
  'manycam',
  'xsplit',
  'droidcam',
  'epoccam',
];

/** Prefer built-in/front camera; reject known virtual cameras when labels are available. */
export async function preferBuiltInCameraDeviceId() {
  if (!navigator.mediaDevices?.enumerateDevices) {
    return undefined;
  }

  const devices = await navigator.mediaDevices.enumerateDevices();
  const videoInputs = devices.filter((d) => d.kind === 'videoinput');

  const usable = videoInputs.filter((d) => {
    const label = (d.label ?? '').toLowerCase();
    return !VIRTUAL_CAMERA_LABELS.some((blocked) => label.includes(blocked));
  });

  const preferred = usable.find((d) => {
    const label = (d.label ?? '').toLowerCase();
    return (
      label.includes('integrated') ||
      label.includes('built-in') ||
      label.includes('front') ||
      label.includes('facetime')
    );
  });

  return (preferred ?? usable[0] ?? videoInputs[0])?.deviceId;
}
