if vim.g.loaded_open16a_plugin then
    return
end
vim.g.loaded_open16a_plugin = true

vim.filetype.add({
    extension = {
        asm = "open16a",
        o16a = "open16a"
    }
})

vim.api.nvim_create_user_command("Open16AStart", function()
    require("open16a").start()
end, { desc = "Start the Open16A language server for this buffer" })

vim.api.nvim_create_user_command("Open16ARestart", function()
    require("open16a").restart()
end, { desc = "Restart the Open16A language server for this buffer" })

local group = vim.api.nvim_create_augroup("Open16ALanguageServer", { clear = true })
vim.api.nvim_create_autocmd("FileType", {
    group = group,
    pattern = "open16a",
    callback = function()
        require("open16a").start()
    end
})
