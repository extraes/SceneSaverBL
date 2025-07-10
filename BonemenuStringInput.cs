namespace SceneSaverBL;

public static class BonemenuStringInput
{
    public const string ALL_NUMBERS_ALLOWED = "*";
    public const string ALL_LETTERS_ALLOWED = "*";
    public const string DEFAULT_SYMBOL_SELECTION = " !~@#$%^&*()_+-=/<>,.;':\"[]{}?";

    const string CATEGORY_NAME_BASE = "Input String: ";
    static readonly string CharsNotForPath = new(Path.GetInvalidFileNameChars());
    static readonly string SymbolsForPath = new(DEFAULT_SYMBOL_SELECTION.Where(c => !CharsNotForPath.Contains(c)).ToArray());
    static Page basePage;
    static BoolElement capsLockElement;
    static Page letters;
    static Page numbers;
    static Page symbols;
    static FunctionElement backspaceElement;
    static FunctionElement confirmElement;
    static int lastLetterSelHash;
    static int lastNumSelHash;
    static int lastSymbolSelHash;

    static UniTaskCompletionSource<string>? uniTaskCompletion;

    static readonly StringBuilder stringCollector = new();
    static bool capsLockActive;

    internal static void Init()
    {
        basePage = Page.Root.CreatePage(CATEGORY_NAME_BASE, Color.white);
        var element = basePage.Parent.Elements.First(e => e.ElementName == basePage.Name);
        basePage.Parent.Remove(element);
        basePage.RemoveAll();
        capsLockElement = basePage.CreateBool("caps", Color.gray, false, SetCapsLock);
        letters = basePage.CreatePage("Letters", Color.white);
        numbers = basePage.CreatePage("Numbers", Color.white);
        symbols = basePage.CreatePage("Symbols/other", Color.white);
        backspaceElement = basePage.CreateFunction("Backspace", Color.red, Backspace);
        confirmElement = basePage.CreateFunction("Confirm", Color.green, ConfirmSelection);
    }

    internal static Task<string> GetFileNameInput() => GetStringInput("*", "*", SymbolsForPath);

    internal static async Task<string> GetStringInput(string lettersSelection = "*", string numbersSelection = "*", string symbolSelection = DEFAULT_SYMBOL_SELECTION)
    {
        basePage.RemoveAll();

        await SetSubpanelButtons(lettersSelection, numbersSelection, symbolSelection);

        RefreshBaseCategory();

#if DEBUG
        if (basePage.Elements.Count == 2) // confirm and backspace elements
        {
            SceneSaverBL.Warn("!!! Bonemenu string input has no values!!! Why are you??? Do not do this!!!");
            SceneSaverBL.WarnVariable(lettersSelection);
            SceneSaverBL.WarnVariable(numbersSelection);
            SceneSaverBL.WarnVariable(symbolSelection);
        }

        if (!lettersSelection.All(char.IsLetter))
        {
            SceneSaverBL.Warn("!!! Bonemenu string input letter selection has NON-LETTER character!");
            SceneSaverBL.WarnVariable(lettersSelection);
        }

        if (!numbersSelection.All(char.IsDigit))
        {
            SceneSaverBL.Warn("!!! Bonemenu string input number selection has NON-DIGIT character!");
            SceneSaverBL.WarnVariable(numbersSelection);
        }

        if (!symbolSelection.All(char.IsSymbol))
        {
            SceneSaverBL.Warn("!!! Bonemenu string input symbol selection has NON-SYMBOL character!");
            SceneSaverBL.WarnVariable(symbolSelection);
        }
#endif

        uniTaskCompletion = new();

        Page currentPage = Menu.CurrentPage;
        Menu.OpenPage(basePage);

        // ConfirmSelection should complete the task when the user selects it
        string res = await uniTaskCompletion.Task;
        uniTaskCompletion = null;

        // return the user back to the original category
        Menu.OpenPage(currentPage);

        return res;
    }

    private static async Task SetSubpanelButtons(string lettersSelection, string numbersSelection, string symbolSelection)
    {
        SetCapsLock(false);

        // for letters, numbers, and symbols, avoid clearing the categories if the same selection will be displayed to the user
        // this will speed up deployment by a couple frames, but hey every ms matters
        int lettersHash = lettersSelection.GetHashCode();
        if (lettersHash != lastLetterSelHash)
        {
            lastLetterSelHash = lettersHash;
            letters.RemoveAll();
            foreach (char c in lettersSelection == "*" ? "abcdefghijklmnopqrstuvwxyz" : lettersSelection)
            {
                letters.CreateFunction(c.ToString(), Color.white, () => InputCharacter(capsLockActive ? char.ToUpper(c) : c));
            }

            await UniTask.Yield();
        }

        int numbersHash = numbersSelection.GetHashCode();
        if (numbersHash != lastNumSelHash)
        {
            lastNumSelHash = numbersHash;
            numbers.RemoveAll();
            foreach (char c in numbersSelection == "*" ? "0123456789" : numbersSelection)
            {
                numbers.CreateFunction(c.ToString(), Color.white, () => InputCharacter(c));
            }

            await UniTask.Yield();
        }

        int symbolHash = symbolSelection.GetHashCode();
        if (symbolHash != lastSymbolSelHash)
        {
            lastSymbolSelHash = symbolHash;
            symbols.RemoveAll();
            foreach (char c in symbolSelection)
            {
                string displayName = c switch
                {
                    '\n' => "Newline",
                    '\t' => "Tab",
                    ' ' => "Space",
                    '_' => "Underscore",
                    '"' => "Double quote/Quotation mark",
                    '\'' => "Single quote/apostraphe",
                    _ => c.ToString(),
                };
                symbols.CreateFunction(displayName, Color.white, () => InputCharacter(c));
            }

            await UniTask.Yield();
        }
    }

    static void RefreshBaseCategory()
    {
        // don't add back the elements if they're empty lol
        if (letters.Elements.Count != 0)
        {
            basePage.Add(capsLockElement); // only letters are affected by caps lock
            basePage.CreatePageLink(letters);
        }

        if (numbers.Elements.Count != 0)
            basePage.CreatePageLink(numbers);
        if (symbols.Elements.Count != 0)
            basePage.CreatePageLink(symbols);
        
        basePage.Add(backspaceElement);
        basePage.Add(confirmElement);
    }

    static void InputCharacter(char c)
    {
        stringCollector.Append(c);
        basePage.Name = CATEGORY_NAME_BASE + stringCollector.ToString();
    }

    static void SetCapsLock(bool capsLock)
    {
        capsLockActive = capsLock;
        if (capsLock)
        {
            capsLockElement.ElementColor = Color.white;
            capsLockElement.ElementName = "CAPS";
        }
        else
        {
            capsLockElement.ElementColor = Color.gray;
            capsLockElement.ElementName = "caps";
        }
    }

    static void Backspace()
    {
        stringCollector.Remove(stringCollector.Length - 1, 1);
        basePage.Name = CATEGORY_NAME_BASE + stringCollector.ToString();
    }

    static void ConfirmSelection()
    {
#if DEBUG
        if (uniTaskCompletion == null)
            throw new NullReferenceException("Unitask completion should not be null when confirm is pressed!");
#endif
        uniTaskCompletion.TrySetResult(stringCollector.ToString());
    }
}
