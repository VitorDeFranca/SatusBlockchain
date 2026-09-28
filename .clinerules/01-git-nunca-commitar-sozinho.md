# Git: nunca commitar por iniciativa própria

Regra permanente deste repositório. Vale para **toda tarefa, em qualquer modo**
(Plan, Act, Yolo). Em caso de dúvida, a resposta é sempre a mesma: **não execute**.

## Proibido por iniciativa própria

Nunca execute como parte de "concluir uma etapa" ou "deixar tudo pronto para revisão":

- `git add`, `git commit`, `git push`, `git fetch`, `git pull`
- `git merge`, `git rebase`, `git cherry-pick`, `git revert`
- `git reset`, `git stash`, `git clean`, `git checkout -- <arquivo>`, `git restore`
- `git tag`, `git branch -d/-m`, `git remote set-url`, `git config` (gravação)

Ou seja: não crie commits, não faça *stage* de arquivos e não envie nada ao GitHub.
Terminar o trabalho **não** implica registrar as mudanças no Git.

## O que fazer no lugar

1. Ao terminar, **apenas informe** que existem alterações pendentes.
2. Mostre o resumo (arquivos criados/alterados) e, se fizer sentido, ofereça os
   comandos prontos em bloco de código — **sem executá-los**.
3. Pare ali. As mudanças ficam no disco; o usuário decide quando e como versionar.

## Quando é permitido

Somente quando a mensagem do usuário pedir **explicitamente**: "commite",
"faça o commit", "commite e envie", "push", "desfaça o último commit", etc.

Mesmo nesse caso:

- faça exatamente o que foi pedido (não emende commits, não faça push forçado,
  não use `--no-verify`, não reescreva histórico);
- **não** inclua arquivos que o usuário não pediu (ex.: `.vscode/`);
- informe o resultado (hash/mensagem) e deixe o `push` por conta dele, salvo
  pedido explícito de envio.

## Permitido sempre (somente leitura)

`git status`, `git log`, `git diff`, `git show`, `git remote -v`, `git branch`,
`git ls-files`, `git config --get` (consulta).

## Contexto

O usuário revisa e envia as mudanças pelo painel **Source Control** do VS Code —
o `git push` deste terminal fica preso no login do Credential Manager. E, conforme
`docs/ROADMAP.md`, cada etapa termina com **pausa para aprovação**, não com
commit automático.