import QRCode from "qrcode";
import { useEffect, useState } from "react";

/** A ticket's QR code, drawn in the browser so it shows offline too. */
export function Qr({ code, size = 200, label }: { code: string; size?: number; label: string }) {
  const [src, setSrc] = useState<string | null>(null);
  useEffect(() => {
    let live = true;
    QRCode.toDataURL(code, { errorCorrectionLevel: "M", margin: 2, width: size * 2, color: { dark: "#1A1A1A", light: "#FFFFFF" } })
      .then((url) => { if (live) setSrc(url); }, () => { if (live) setSrc(null); });
    return () => { live = false; };
  }, [code, size]);

  return src
    ? <img className="qr" src={src} width={size} height={size} alt={label} />
    : <div className="qr skeleton" style={{ width: size, height: size }} aria-hidden="true" />;
}
