"""Recognize internal delivery requests with either Alice invocation form.

Matching the full utterance is deliberate: ordinary questions about delivery
must never become a Station control request. Ownership and nonce validity are
still checked by AliceBridge/StationDelivery.
"""
import re

_DELIVERY=re.compile(r'(?:алиса )?(?P<launch>скажи (?:навыку )?(?:глубокий разум|моя лада|моей ладе|мою ладу|лада|ладе|ладу) )?доставка (?P<number>[0-9 ]{1,64})')

def is_launch(text):
    return bool(re.fullmatch(r'(?:алиса )?(?:включи|запусти|открой)(?: навык)? (?:моя лада|мою ладу|лада|ладу|глубокий разум)',normalized(text)))

def normalized(text):
    return re.sub(r'\s+',' ',re.sub(r'[^\w\s]','',text.lower())).strip()

def delivery_command(text):
    """Return (reserved_command, valid_12_digit_nonce_or_None)."""
    match=_DELIVERY.fullmatch(normalized(text))
    if not match:return False,None
    number=match['number'].replace(' ','')
    if not match['launch'] and len(number)<12:return False,None
    return True,number if len(number)==12 else None
