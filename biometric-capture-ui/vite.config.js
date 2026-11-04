import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Content-Security-Policy for the page the Tray App's WebView2 loads. Only built output gets it:
// the Vite dev server injects inline scripts (React refresh) that this policy would block.
// - script-src: the bundle only; 'wasm-unsafe-eval' lets the face-detection WebAssembly compile.
// - connect-src: AWS Rekognition liveness streaming and its model CDN (*.amazonaws.com), plus the
//   TensorFlow model hosts the detector can fall back to.
// - media-src mediastream:/blob: for the camera preview; worker-src blob: for the TF.js workers.
// The config the tray pushes with ExecuteScriptAsync is not affected by CSP.
export const CONTENT_SECURITY_POLICY = [
  "default-src 'self'",
  "script-src 'self' 'wasm-unsafe-eval'",
  "worker-src 'self' blob:",
  "connect-src 'self' blob: data: https://*.amazonaws.com wss://*.amazonaws.com https://*.amazonaws.com.cn wss://*.amazonaws.com.cn https://tfhub.dev https://www.kaggle.com https://storage.googleapis.com",
  "img-src 'self' data: blob:",
  "media-src 'self' blob: mediastream:",
  "style-src 'self' 'unsafe-inline'",
  "font-src 'self' data:",
  "object-src 'none'",
  "base-uri 'none'",
  "form-action 'none'",
  "frame-src 'none'",
].join('; ')

const contentSecurityPolicy = () => ({
  name: 'onevo-content-security-policy',
  apply: 'build',
  transformIndexHtml: () => [
    {
      tag: 'meta',
      attrs: { 'http-equiv': 'Content-Security-Policy', content: CONTENT_SECURITY_POLICY },
      injectTo: 'head-prepend',
    },
  ],
})

export default defineConfig({
  plugins: [react(), contentSecurityPolicy()],
  base: './',
  build: {
    outDir: 'dist',
    emptyOutDir: true,
  },
})
