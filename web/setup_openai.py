#!/usr/bin/env python3
"""Run interactively in PuTTY. The key never enters shell history, arguments or logs."""
from pathlib import Path
import getpass
import os
import sys
import tempfile


def main():
    if not sys.stdin.isatty():
        raise SystemExit('Запусти эту команду непосредственно в PuTTY со скрытым вводом.')
    os.umask(0o077)
    folder=Path.home()/'.config/deepbrain'
    folder.mkdir(parents=True,exist_ok=True,mode=0o700)
    folder.chmod(0o700)
    target=folder/'openai-api-key'
    key=getpass.getpass('Вставь API-ключ OpenAI (символы не отображаются): ').strip()
    if not key and target.exists():
        print('Существующий ключ оставлен без изменений.');return
    if not key.startswith('sk-') or not 20<=len(key)<=16000 or any(c.isspace() for c in key):
        raise SystemExit('Формат ключа не распознан. Файл не изменён.')
    fd,name=tempfile.mkstemp(prefix='.openai-key-',dir=folder)
    try:
        with os.fdopen(fd,'w',encoding='utf-8') as f:
            f.write(key+'\n');f.flush();os.fsync(f.fileno())
        os.replace(name,target)
    finally:
        if Path(name).exists():Path(name).unlink()
    print('Ключ сохранён с доступом только для текущего пользователя. Перезапуск DeepBrain не нужен.')
    print('Проверка подключения будет выполнена отдельно; платных запросов эта команда не отправляла.')


if __name__=='__main__':main()
