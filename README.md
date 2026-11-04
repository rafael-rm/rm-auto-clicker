<img src="assets/branding/rm-auto-clicker-logo.png" alt="Logo do RM Auto Clicker" width="80">

# RM Auto Clicker

Automatize cliques no Windows com velocidade ajustável, atalhos personalizáveis e uma interface simples.

## Como usar

1. Na seção **Releases** do repositório, baixe o arquivo `RMAutoClicker-<versão>-win-x64.exe`.
2. Abra o aplicativo e escolha a velocidade e o botão do mouse.
3. Posicione o cursor onde deseja clicar e pressione **F8** para iniciar.
4. Pressione **F8** novamente para parar.

Também é possível usar os botões **Iniciar** e **Parar**. A parada de emergência é **Ctrl+Shift+F10**.

O aplicativo é distribuído em um único EXE para **Windows 10 ou 11 de 64 bits (x64)**. Não precisa de instalação nem do .NET instalado separadamente.

## Recursos

- Velocidade de **1 a 25 cliques por segundo**.
- Clique com o botão esquerdo, direito ou do meio.
- Atalho de iniciar/parar personalizável, com combinações de Ctrl, Alt e Shift.
- Opções avançadas de atraso inicial, limite de cliques, limite de tempo e variação do intervalo.
- Contadores de cliques e tempo da sessão.
- Temas claro, escuro e do sistema.
- Interface em português, inglês e espanhol.

O atraso inicial vem desativado. Os cliques são suspensos enquanto o cursor estiver sobre a janela do próprio aplicativo e retomados ao sair dela.

## Preferências e bandeja

Em **Preferências**, escolha o tema e o idioma. Depois de trocar o idioma, use **Reiniciar agora** para aplicá-lo. A opção **Minimizar para a bandeja** fica desativada por padrão e é salva ao selecioná-la.

Para mudar o atalho, clique em **Alterar atalho**, pressione a combinação desejada e confirme em **Aplicar atalho**. Se a combinação estiver indisponível, o atalho anterior continua valendo.

Por padrão, minimizar mantém a janela na barra de tarefas. Ative **Minimizar para a bandeja** para ocultá-la da barra e deixá-la na área de notificação; para reabrir, dê dois cliques no ícone próximo ao relógio ou escolha **Abrir** no menu do ícone. Fechar a janela encerra o aplicativo e os cliques.

## Configurações e logs

Os arquivos ficam em:

```text
%LOCALAPPDATA%\RM Corporation\RM Auto Clicker\
```

As preferências são salvas em `settings.json` e os logs ficam em `logs`.

## Documentação técnica

Para compilar, testar ou contribuir com o projeto, consulte o [guia técnico](docs/technical.md).
