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

local function workspace_server_path(buffer_name)
    local directory = vim.fs.dirname(buffer_name)
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

local function server_path(buffer_name)
    return options.server_path
        or vim.g.open16a_lsp_path
        or vim.env.OPEN16A_LSP_PATH
        or workspace_server_path(buffer_name)
end

local function root_dir(buffer_name)
    local markers = vim.fs.find({ "OldSimulator.sln", ".git" }, {
        path = vim.fs.dirname(buffer_name),
        upward = true,
        stop = vim.uv.os_homedir()
    })
    return markers[1] and vim.fs.dirname(markers[1]) or vim.fs.dirname(buffer_name)
end

function M.setup(user_options)
    options = vim.tbl_deep_extend("force", vim.deepcopy(defaults), user_options or {})
end

function M.start(buffer)
    buffer = buffer or vim.api.nvim_get_current_buf()
    local buffer_name = vim.api.nvim_buf_get_name(buffer)
    if buffer_name == "" then
        return
    end

    local path = server_path(buffer_name)
    if not exists(path) then
        if not warned_missing_server then
            warned_missing_server = true
            vim.notify(
                "Open16A-LSP.dll was not found. Set require('open16a').setup({ server_path = '...' }).",
                vim.log.levels.WARN
            )
        end
        return
    end

    vim.lsp.start({
        name = "open16a-lsp",
        cmd = { options.dotnet_path, path },
        root_dir = root_dir(buffer_name),
        capabilities = vim.lsp.protocol.make_client_capabilities()
    }, { bufnr = buffer })
end

function M.restart(buffer)
    buffer = buffer or vim.api.nvim_get_current_buf()
    for _, client in ipairs(vim.lsp.get_clients({ bufnr = buffer, name = "open16a-lsp" })) do
        client:stop()
    end
    vim.schedule(function()
        M.start(buffer)
    end)
end

return M
