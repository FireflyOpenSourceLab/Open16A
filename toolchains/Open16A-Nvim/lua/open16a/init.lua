local M = {}

local defaults = {
    server_path = nil,
    dotnet_path = "dotnet"
}

local options = vim.deepcopy(defaults)
local warned_missing_server = false

local function exists(path)
    return path and vim.uv.fs_stat(path) ~= nil
end

local function workspace_server_path(start_path)
    local directory = vim.fs.dirname(start_path)
    while directory and directory ~= "" do
        local candidate = vim.fs.joinpath(directory, "toolchains", "Open16A-LSP", "bin", "Debug", "net10.0", "Open16A-LSP.dll")
        if exists(candidate) then
            return candidate
        end

        local parent = vim.fs.dirname(directory)
        if parent == directory then
            break
        end
        directory = parent
    end

    return nil
end

local function bundled_server_path()
    local source = debug.getinfo(1, "S").source:sub(2)
    local plugin_root = vim.fs.dirname(vim.fs.dirname(vim.fs.dirname(source)))
    local sysname = vim.uv.os_uname().sysname
    local relative = sysname == "Windows_NT"
        and { "lsp", "win-x64", "Open16A-LSP.exe" }
        or sysname == "Linux"
            and { "lsp", "linux-x64", "Open16A-LSP" }
            or sysname == "Darwin"
                and { "lsp", "osx-arm64", "Open16A-LSP" }
            or nil
    if not relative then
        return nil
    end

    local candidate = vim.fs.joinpath(plugin_root, unpack(relative))
    return exists(candidate) and candidate or nil
end

local function server_path(buffer)
    local buffer_name = buffer and vim.api.nvim_buf_get_name(buffer) or ""
    return options.server_path
        or vim.g.open16a_lsp_path
        or vim.env.OPEN16A_LSP_PATH
        or bundled_server_path()
        or (buffer_name ~= "" and workspace_server_path(buffer_name))
        or workspace_server_path(vim.fn.getcwd())
end

function M.setup(user_options, buffer)
    options = vim.tbl_deep_extend("force", vim.deepcopy(defaults), user_options or {})

    local path = server_path(buffer)
    if not exists(path) then
        if not warned_missing_server then
            warned_missing_server = true
            vim.notify(
                "Open16A-LSP.dll was not found. Set require('open16a').setup({ server_path = '...' }).",
                vim.log.levels.WARN
            )
        end
        return false
    end

    local command = path:match("%.dll$") and { options.dotnet_path, path } or { path }
    vim.lsp.config("open16a", { cmd = command })
    vim.lsp.enable("open16a")
    return true
end

function M.start(buffer)
    return M.setup(options, buffer)
end

function M.restart()
    vim.cmd("lsp restart open16a")
end

return M
