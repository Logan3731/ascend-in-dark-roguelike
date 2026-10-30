// 한국어 조사: 앞 단어 끝 글자에 받침이 있으면 withBatchim, 없으면 without (도끼를 / 튜닉을, 쥐가 / 슬라임이)
public static class Korean
{
    public static string Josa(string word, string withBatchim, string without)
    {
        if (string.IsNullOrEmpty(word)) return without;
        char c = word[word.Length - 1];
        if (c < 0xAC00 || c > 0xD7A3) return without;
        return (c - 0xAC00) % 28 > 0 ? withBatchim : without;
    }
}
