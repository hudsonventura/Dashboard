# Dashboard

Dashboard local em Avalonia para monitorar câmeras RTSP, geração solar Solis, clima e métricas do sistema.

## Pré-requisitos

- .NET SDK 10.
- `smartmontools` instalado quando o monitoramento de temperatura e saúde dos discos for utilizado.
- As configurações da aplicação ficam em `appsettings.json`.

Para compilar:

```bash
dotnet build Dashboard.csproj
```

## S.M.A.R.T. dos discos sem senha no dashboard

O dashboard obtém a temperatura e o estado de saúde dos discos usando uma consulta equivalente a:

```bash
/usr/sbin/smartctl -j -A -H /dev/nvme0n1
```

Troque `/dev/nvme0n1` pelo dispositivo físico correspondente, como `/dev/sda`. A aplicação reutiliza o resultado por cinco minutos para não consultar S.M.A.R.T. a cada atualização de CPU.

### Diagnóstico inicial

O comando `smartctl` sem um dispositivo apenas confirma que o programa está instalado. Para listar os dispositivos detectados e validar a leitura, execute:

```bash
sudo /usr/sbin/smartctl --scan-open
sudo /usr/sbin/smartctl -j -A -H /dev/nvme0n1
sudo /usr/sbin/smartctl -j -A -H /dev/sda
```

Se a consulta só funcionar com `sudo`, o usuário que iniciou o dashboard não possui acesso de baixo nível ao dispositivo. Além disso, em containers, Snap ou Flatpak, os nós `/dev/nvme*` e `/dev/sd*` precisam estar expostos ao ambiente da aplicação.

### Opção direta: capacidade no binário

É possível permitir que `smartctl` seja executado sem `sudo`:

```bash
sudo setcap cap_dac_override,cap_sys_rawio+ep /usr/sbin/smartctl
getcap /usr/sbin/smartctl
```

O resultado esperado do segundo comando inclui `cap_sys_rawio=ep`. Para remover a permissão:

```bash
sudo setcap -r /usr/sbin/smartctl
```

> Atenção: essa capacidade se aplica ao binário inteiro. Ela não limita o uso a consultas e pode ser removida por atualizações do pacote `smartmontools`.

### Opção recomendada: helper restrito a consultas

Para manter o acesso mínimo, use um helper local executado com privilégio administrativo que aceite somente os argumentos `-j -A -H` e uma lista explícita de dispositivos permitidos. O dashboard chama o helper sem interação; o helper executa `smartctl` e devolve somente o JSON.

O helper deve:

1. Ser de propriedade de `root` e não ser gravável pelo usuário do dashboard.
2. Validar o dispositivo contra uma lista permitida, por exemplo `/dev/nvme0n1` e `/dev/sda`.
3. Executar exclusivamente `/usr/sbin/smartctl -j -A -H <dispositivo>`.
4. Receber uma regra de `sudoers` limitada ao próprio helper, com `NOPASSWD`; nunca uma regra ampla para o comando `smartctl`.

Esse modelo evita dar ao dashboard permissão para executar outros argumentos de S.M.A.R.T. ou comandos administrativos.
