#!/usr/bin/env python3
"""
Fantasy Aquarium 存档解密 / 加密工具

游戏: Fantasy Aquarium (Steam appid 3183350)
存档目录: %USERPROFILE%/AppData/LocalLow/MoonscapeStudio/Fantasy Aquarium/
WSL 路径: /mnt/c/Users/<用户名>/AppData/LocalLow/MoonscapeStudio/Fantasy Aquarium/

格式: AES-128-CBC，密文整体做 Base64 后写文件；密钥/IV 硬编码在
      Assembly-CSharp.dll -> SaveLoadManager._encryptionKey / _encryptionIV
      = UTF8("luoyanwen7654321")  (16 字节)

用法:
    python3 save_edit.py list                       # 列出存档文件与大小
    python3 save_edit.py show appdata_0_0.dat       # 解密并打印 JSON
    python3 save_edit.py decrypt appdata_0_0.dat out.json
    python3 save_edit.py encrypt out.json appdata_0_0.dat   # 写回(自动备份 .bak)

注意: 改存档前先退出游戏，备份原文件；游戏开启了 Steam 云同步
      (steam_autocloud.vdf)，本地存档可能被云端覆盖。
"""

import argparse
import base64
import json
import os
import shutil
import sys

KEY = b"luoyanwen7654321"  # 16 字节 -> AES-128
IV = b"luoyanwen7654321"

DEFAULT_DIRS = [
    # WSL 下访问 Windows 用户目录
    *[os.path.join("/mnt/c/Users", u, "AppData/LocalLow/MoonscapeStudio/Fantasy Aquarium")
      for u in (os.listdir("/mnt/c/Users") if os.path.isdir("/mnt/c/Users") else [])],
    # 原生 Windows 运行
    os.path.expandvars(r"%USERPROFILE%\AppData\LocalLow\MoonscapeStudio\Fantasy Aquarium"),
]


def find_dir(explicit=None):
    if explicit:
        return explicit
    for d in DEFAULT_DIRS:
        if d and os.path.isdir(d):
            return d
    sys.exit("找不到存档目录，请用 --dir 指定")


def _aes():
    try:
        from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
    except ImportError:
        return None
    return Cipher(algorithms.AES(KEY), modes.CBC(IV))


def _unpad(b: bytes) -> bytes:
    """去掉 PKCS7 填充（游戏用 CryptoStream + StreamWriter 写入，末尾有填充块）"""
    if not b:
        return b
    pad = b[-1]
    if 1 <= pad <= 16 and b.endswith(bytes([pad]) * pad):
        return b[:-pad]
    return b


def decrypt(raw: bytes) -> str:
    data = base64.b64decode(raw)
    c = _aes()
    if c is None:
        # 退路：调用系统 openssl
        import subprocess
        p = subprocess.run(["openssl", "enc", "-d", "-aes-128-cbc",
                            "-K", KEY.hex(), "-iv", IV.hex()],
                           input=data, capture_output=True)
        if p.returncode != 0:
            raise RuntimeError(p.stderr.decode())
        return p.stdout.decode("utf-8")
    d = c.decryptor()
    return _unpad(d.update(data) + d.finalize()).decode("utf-8")


def encrypt(text: str) -> bytes:
    data = text.encode("utf-8")
    pad = 16 - len(data) % 16
    data += bytes([pad]) * pad  # PKCS7
    c = _aes()
    if c is None:
        import subprocess
        p = subprocess.run(["openssl", "enc", "-e", "-aes-128-cbc",
                            "-K", KEY.hex(), "-iv", IV.hex()],
                           input=data, capture_output=True)
        if p.returncode != 0:
            raise RuntimeError(p.stderr.decode())
        return base64.b64encode(p.stdout)
    e = c.encryptor()
    return base64.b64encode(e.update(data) + e.finalize())


def main():
    ap = argparse.ArgumentParser(description="Fantasy Aquarium 存档加解密")
    ap.add_argument("--dir", help="存档目录")
    sub = ap.add_subparsers(dest="cmd", required=True)

    sub.add_parser("list")
    p_show = sub.add_parser("show")
    p_show.add_argument("file")
    p_dec = sub.add_parser("decrypt")
    p_dec.add_argument("file")
    p_dec.add_argument("out")
    p_enc = sub.add_parser("encrypt")
    p_enc.add_argument("jsonfile")
    p_enc.add_argument("file")

    a = ap.parse_args()
    d = find_dir(a.dir)

    if a.cmd == "list":
        for n in sorted(os.listdir(d)):
            p = os.path.join(d, n)
            if os.path.isfile(p):
                print(f"  {n:28s} {os.path.getsize(p):>8d} B")
        return

    if a.cmd == "show":
        print(decrypt(open(os.path.join(d, a.file), "rb").read()))
    elif a.cmd == "decrypt":
        txt = decrypt(open(os.path.join(d, a.file), "rb").read())
        open(a.out, "w", encoding="utf-8").write(txt)
        print(f"已写出 {a.out} ({len(txt)} 字符)")
    elif a.cmd == "encrypt":
        txt = open(a.jsonfile, encoding="utf-8").read()
        json.loads(txt)  # 校验 JSON 合法
        target = os.path.join(d, a.file)
        if os.path.exists(target):
            shutil.copy2(target, target + ".bak")
            print(f"已备份 {target}.bak")
        open(target, "wb").write(encrypt(txt))
        print(f"已写入 {target}")


if __name__ == "__main__":
    main()
