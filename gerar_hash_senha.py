import base64, getpass, hashlib, os

senha = getpass.getpass("Digite a nova senha (nao aparece na tela): ")
if getpass.getpass("Repita a senha: ") != senha:
    raise SystemExit("As senhas nao conferem.")
sal = os.urandom(16)
h = hashlib.pbkdf2_hmac("sha256", senha.encode(), sal, 100000, 32)
print()
print("Copie a linha abaixo e cole em SenhaHash no appsettings.json:")
print(f"pbkdf2$100000${base64.b64encode(sal).decode()}${base64.b64encode(h).decode()}")
