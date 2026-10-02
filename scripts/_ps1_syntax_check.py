#!/usr/bin/env python3
"""
Vérificateur syntaxique minimal pour fichiers PowerShell (.ps1), utilisé UNIQUEMENT en interne
pour auditer scripts/publish-release.ps1 dans un environnement sans PowerShell installé.

Ce n'est PAS un vrai parseur PowerShell (il ne connaît aucune cmdlet ni grammaire complète) :
il modélise seulement les règles de bas niveau qui causent le plus souvent des erreurs de syntaxe
« accolade/paranthèse/chaîne non terminée » signalées par le véritable interpréteur PowerShell :
  - chaînes entre apostrophes '...' (doublement '' = apostrophe littérale, pas d'autre échappement) ;
  - chaînes entre guillemets "..." (échappement par backtick `, sous-expressions $(...) et ${...}
    imbriquées correctement comptées) ;
  - commentaires # jusqu'à fin de ligne et blocs <# ... #> ;
  - continuation de ligne par backtick en toute fin de ligne (signale une erreur si un backtick de
    continuation est suivi d'autre chose qu'une fin de ligne, le piège PowerShell le plus classique) ;
  - équilibre global de {, }, (, ), [, ] en dehors des chaînes/commentaires.

Usage : python3 scripts/_ps1_syntax_check.py scripts/publish-release.ps1
"""
import sys


def check(path: str) -> int:
    with open(path, "r", encoding="utf-8-sig") as f:
        text = f.read()

    errors = []
    stack = []  # (char, line, col)
    pairs = {')': '(', ']': '[', '}': '{'}
    openers = set(pairs.values())

    i = 0
    n = len(text)
    line = 1
    col = 1

    def advance(count=1):
        nonlocal i, line, col
        for _ in range(count):
            if i < n and text[i] == "\n":
                line += 1
                col = 1
            else:
                col += 1
            i += 1

    while i < n:
        ch = text[i]

        # Bloc de commentaire <# ... #>
        if ch == '<' and text[i:i + 2] == '<#':
            end = text.find('#>', i + 2)
            if end == -1:
                errors.append((line, col, "Bloc de commentaire <# ... #> jamais fermé."))
                break
            advance(end + 2 - i)
            continue

        # Commentaire de fin de ligne #
        if ch == '#':
            nl = text.find('\n', i)
            if nl == -1:
                break
            advance(nl - i)
            continue

        # Chaîne entre apostrophes '...'
        if ch == "'":
            start_line, start_col = line, col
            advance(1)
            closed = False
            while i < n:
                if text[i] == "'":
                    if i + 1 < n and text[i + 1] == "'":
                        advance(2)
                        continue
                    advance(1)
                    closed = True
                    break
                advance(1)
            if not closed:
                errors.append((start_line, start_col, "Chaîne entre apostrophes jamais terminée."))
            continue

        # Chaîne entre guillemets "..."
        if ch == '"':
            start_line, start_col = line, col
            advance(1)
            closed = False
            while i < n:
                c = text[i]
                if c == '`':
                    # Backtick = échappement du caractère suivant à l'intérieur d'une chaîne "...".
                    advance(2 if i + 1 < n else 1)
                    continue
                if c == '"':
                    advance(1)
                    closed = True
                    break
                if c == '$' and i + 1 < n and text[i + 1] in '({':
                    # Sous-expression $( ... ) ou variable ${ ... } : on consomme jusqu'à la
                    # parenthèse/accolade correspondante (gère un seul niveau d'imbrication, ce qui
                    # couvre tous les cas réellement utilisés dans ce script).
                    opench = text[i + 1]
                    closech = ')' if opench == '(' else '}'
                    depth = 1
                    advance(2)
                    while i < n and depth > 0:
                        if text[i] == opench:
                            depth += 1
                        elif text[i] == closech:
                            depth -= 1
                        advance(1)
                    continue
                advance(1)
            if not closed:
                errors.append((start_line, start_col, "Chaîne entre guillemets jamais terminée."))
            continue

        # Continuation de ligne par backtick : valide SEULEMENT si immédiatement suivi d'un \n
        # (ou \r\n). Tout espace/tabulation entre le backtick et la fin de ligne casse silencieusement
        # la continuation en PowerShell : c'est le piège le plus fréquent (voir pourquoi ce script a
        # été entièrement réécrit sans aucune continuation par backtick).
        if ch == '`':
            j = i + 1
            if j < n and text[j] == '\r':
                j += 1
            if j >= n or text[j] != '\n':
                errors.append((line, col, "Backtick isolé hors chaîne (jamais utilisé volontairement ici)."))
            advance(1)
            continue

        if ch in openers:
            stack.append((ch, line, col))
            advance(1)
            continue

        if ch in pairs:
            if not stack or stack[-1][0] != pairs[ch]:
                errors.append((line, col, f"'{ch}' sans ouverture correspondante."))
            else:
                stack.pop()
            advance(1)
            continue

        advance(1)

    for (c, l, co) in stack:
        errors.append((l, co, f"'{c}' jamais fermé."))

    if errors:
        print(f"ÉCHEC : {len(errors)} problème(s) détecté(s) dans {path} :")
        for (l, co, msg) in sorted(errors):
            print(f"  Ligne {l}, colonne {co} : {msg}")
        return 1

    print(f"OK : {path} - accolades/parenthèses/crochets/chaînes/continuations équilibrés "
          f"({len(text.splitlines())} lignes analysées).")
    return 0


if __name__ == "__main__":
    sys.exit(check(sys.argv[1]))
