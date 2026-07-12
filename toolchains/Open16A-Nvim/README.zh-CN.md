# Open16A Neovim 插件

这个插件按当前 `nvim-lspconfig` 配置约定提供 `open16a` server config，并以 Neovim 0.11+ 的 `vim.lsp.config` / `vim.lsp.enable` API 启用它。它为 `.o16a` 和 `.asm` 自动设置 `open16a` filetype。

先构建 LSP：

```sh
cd /mnt/d/sim/OldSimulator
dotnet build toolchains/Open16A-LSP
```

以 `lazy.nvim` 为例，在配置中加入：

```lua
{
  dir = "/mnt/d/sim/OldSimulator/toolchains/Open16A-Nvim",
  dependencies = { "neovim/nvim-lspconfig" },
  config = function()
    require("open16a").setup({
      server_path = "/mnt/d/sim/OldSimulator/toolchains/Open16A-LSP/bin/Debug/net10.0/Open16A-LSP.dll"
    })
  end
}
```

若不指定 `server_path`，插件会优先从当前缓冲区路径、再从 Neovim 当前工作目录向上查找工作区中的 `toolchains/Open16A-LSP/bin/Debug/net10.0/Open16A-LSP.dll`。也可设置环境变量 `OPEN16A_LSP_PATH` 或全局变量 `vim.g.open16a_lsp_path`。

命令：

- `:Open16AStart`：为当前缓冲区启动服务器。
- `:Open16ARestart`：重启当前缓冲区的服务器。

要求 Neovim 0.11+、`nvim-lspconfig` 和可执行的 `dotnet`。需要自定义 `dotnet` 路径时，调用 `setup({ dotnet_path = "/usr/sbin/dotnet" })`。
