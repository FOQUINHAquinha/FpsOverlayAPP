# FPS Overlay

App para Windows que mostra um overlay de desempenho por cima dos jogos, no estilo do Afterburner/RTSS:

- FPS em tempo real, **1% low**, **0.1% low** e **média**
- Gráfico de frametime e frametime atual em ms
- GPU: uso, temperatura, potência, VRAM e clock (NVIDIA, AMD ou Intel, detectada automaticamente)
- CPU: uso, temperatura, potência e clock (Intel ou AMD, detectada automaticamente)
- RAM e API gráfica do jogo (DX9/11/12, Vulkan, OpenGL)

Tem um painel de controle. Ao fechar o painel, o app continua rodando na bandeja (perto do relógio). Para sair de vez, use **Sair** no ícone da bandeja ou o botão **Sair do app**.

## Como gerar o .exe pelo GitHub

1. Entre em https://github.com/new, dê um nome (ex.: `fps-overlay`) e clique em **Create repository**.
2. Na página do repositório, clique em **uploading an existing file**.
3. Arraste **todo o conteúdo** desta pasta, incluindo a pasta `.github`, e clique em **Commit changes**.
4. Abra a aba **Actions**. A compilação começa sozinha e leva uns 3 minutos.
5. Quando ficar verde, clique na execução e baixe **FPSOverlay** em *Artifacts*. Dentro do zip está o `FPSOverlay.exe`.

O .exe é um arquivo único: não precisa instalar .NET nem Python, e o PresentMon (ferramenta da Intel que mede os frames) já vai embutido.

**Opcional, link fixo de download:** crie uma tag `v1.0` (em *Releases > Create a new release*). O workflow publica o .exe na página de Releases.

## Uso

- Abra o `FPSOverlay.exe` e aceite o pedido de administrador. Ele é necessário para medir os frames e ler os sensores.
- Abra o jogo, de preferência em **tela cheia sem bordas (borderless)**. O overlay segue automaticamente o jogo em primeiro plano.
- No painel dá para mudar posição, tamanho, transparência, seções visíveis e o cálculo do 1%/0.1%, além de ativar **Iniciar junto com o Windows**.

| Atalho | Ação |
|---|---|
| Ctrl+Shift+F10 | Mostrar / ocultar o overlay |
| Ctrl+Shift+F11 | Zerar estatísticas |
| Ctrl+Shift+F12 | Mover o overlay (arraste; aperte de novo para travar) |

## Temperatura, potência e clock da CPU

O Windows só libera esses sensores por meio de um driver. Instale o **PawnIO** (gratuito, o mesmo que o LibreHardwareMonitor e o FanControl usam) em https://pawnio.eu e reabra o app. Enquanto ele não estiver instalado, o painel mostra um aviso com o botão de download. Os dados da GPU funcionam sem ele.

## Observações

- Nenhum overlay externo aparece por cima de jogos em **tela cheia exclusiva**. Use borderless.
- O app não injeta nada no jogo: é uma janela separada e o PresentMon usa eventos do próprio Windows. Alguns anti-cheats muito rígidos (como o FACEIT) já tiveram problema com o driver PawnIO. Nesses casos, dá para usar sem ele.
- As configurações ficam em `%APPDATA%\FpsOverlay\settings.json`, e os erros (se houver) em `erros.log`, na mesma pasta.
