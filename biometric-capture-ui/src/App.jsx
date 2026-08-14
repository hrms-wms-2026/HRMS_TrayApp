import { useCallback, useEffect, useState } from 'react';
import { ThemeProvider } from '@aws-amplify/ui-react';
import { FaceLivenessDetectorCore } from '@aws-amplify/ui-react-liveness';
import '@aws-amplify/ui-react/styles.css';
import { getSessionConfig, reportCaptureFinished } from './bridge';

export default function App() {
  const [config, setConfig] = useState(null);

  useEffect(() => {
    let cancelled = false;

    const poll = setInterval(() => {
      const session = getSessionConfig();
      if (!session?.AwsSessionId || cancelled) {
        return;
      }

      clearInterval(poll);
      setConfig(session);
    }, 250);

    return () => {
      cancelled = true;
      clearInterval(poll);
    };
  }, []);

  const credentialProvider = useCallback(async () => {
    const session = getSessionConfig();
    if (!session?.AccessKeyId || !session?.SecretAccessKey || !session?.SessionToken) {
      throw new Error('SESSION_CONFIG_MISSING');
    }

    return {
      accessKeyId: session.AccessKeyId,
      secretAccessKey: session.SecretAccessKey,
      sessionToken: session.SessionToken,
      expiration: new Date(Date.now() + 15 * 60 * 1000),
    };
  }, [config]);

  const onAnalysisComplete = useCallback(async () => {
    reportCaptureFinished(true, null);
  }, []);

  const onError = useCallback((error) => {
    const code = error?.state ?? error?.name ?? 'LIVENESS_ERROR';
    reportCaptureFinished(false, String(code));
  }, []);

  if (!config) {
    return <div className="status-panel">Waiting for capture session…</div>;
  }

  return (
    <ThemeProvider>
      <FaceLivenessDetectorCore
        sessionId={config.AwsSessionId}
        region={config.Region}
        onAnalysisComplete={onAnalysisComplete}
        onError={onError}
        config={{ credentialProvider }}
      />
    </ThemeProvider>
  );
}
