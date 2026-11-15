"""Render a QR locally. No login URL is sent to an image service."""
import io,sys
from pathlib import Path

def svg(link):
    deps=Path('/home/ivan/deepbrain-runtime/station-output-deps')
    if deps.is_dir() and str(deps) not in sys.path:sys.path.insert(0,str(deps))
    import segno
    output=io.BytesIO()
    segno.make_qr(link,error='m').save(output,kind='svg',scale=5,border=4,dark='#000000',light='#ffffff')
    return output.getvalue()
