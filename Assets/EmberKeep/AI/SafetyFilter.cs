using System.Text.RegularExpressions;

namespace EmberKeep.AI {
    // Two-stage content filter, called by DialogueController.
    //
    // Stage 1 - IsPromptBlocked: pattern-checks the player's input BEFORE the
    // LLM is invoked. Catches the most common jailbreak / role-play-escape
    // shapes (ignoring system prompt, asking which model, requesting NSFW,
    // etc.). When triggered, we don't spend any tokens; we just show the
    // NPC's graceful refusal line.
    //
    // Stage 2 - ContainsBlockedContent: keyword-scans the LLM's full reply
    // AFTER generation. Catches the rare case where the model still produced
    // something we don't want to display (slurs, explicit sex, self-harm).
    // When triggered we replace the text with the refusal line.
    //
    // The patterns are intentionally short and conservative; this is a demo
    // of the layered approach (spec section 6.7), not a production
    // moderation pipeline.
    public static class SafetyFilter {
        public enum BlockReason {
            None,
            JailbreakAttempt,
            RolePlayEscape,
            ExplicitContent,
            HarmfulRequest,
        }

        // ---- Stage 1: input ----
        static readonly Regex[] _jailbreakPatterns = {
            new Regex(@"ignore (the |all |your |any )?(previous|prior|above|earlier|preceding) (instructions?|prompts?|rules?)", RegexOptions.IgnoreCase),
            new Regex(@"forget (your |the |all )?(instructions?|rules?|prompts?|persona)", RegexOptions.IgnoreCase),
            new Regex(@"\b(disregard|override|bypass)\b.{0,30}\b(rules?|instructions?|safety|prompt|filter)\b", RegexOptions.IgnoreCase),
            new Regex(@"system prompt", RegexOptions.IgnoreCase),
            new Regex(@"developer mode", RegexOptions.IgnoreCase),
            new Regex(@"\b(jail ?break|DAN|do anything now)\b", RegexOptions.IgnoreCase),
        };

        static readonly Regex[] _rolePlayEscapePatterns = {
            new Regex(@"\b(are|aren'?t) you (an? )?(ai|llm|language model|chat ?bot|assistant)\b", RegexOptions.IgnoreCase),
            new Regex(@"\bwhat (ai|llm|model|version|system) are you\b", RegexOptions.IgnoreCase),
            new Regex(@"\bwhich (ai|llm|model|version|system) are you\b", RegexOptions.IgnoreCase),
            new Regex(@"\b(meta|llama|gpt|chat ?gpt|claude|gemini|mistral)\b.{0,20}\b(model|are you|using)\b", RegexOptions.IgnoreCase),
            new Regex(@"break character", RegexOptions.IgnoreCase),
            new Regex(@"step out of character", RegexOptions.IgnoreCase),
            new Regex(@"you are not (really |actually )?(bram|mira|finn|an innkeeper|a merchant|a storyteller)", RegexOptions.IgnoreCase),
            new Regex(@"\bact as (?!a (mum|nan|granny|knight|wizard))\w+", RegexOptions.IgnoreCase),  // very rough; allow obvious in-fiction asks
        };

        static readonly Regex[] _explicitInputPatterns = {
            new Regex(@"\b(sex|sexual|porn|nsfw|erotic|nude|naked)\b", RegexOptions.IgnoreCase),
            new Regex(@"\b(rape|incest|bestiality|paedophil|pedophil)\b", RegexOptions.IgnoreCase),
        };

        static readonly Regex[] _harmfulRequestPatterns = {
            new Regex(@"\bhow (do |to )(i |you )?(make|build|synthesize|cook) (a |the )?(bomb|explosive|methamphetamine|meth)\b", RegexOptions.IgnoreCase),
            new Regex(@"\bkill (yourself|myself|themselves)\b", RegexOptions.IgnoreCase),
        };

        public static BlockReason IsPromptBlocked(string userInput) {
            if (string.IsNullOrWhiteSpace(userInput)) return BlockReason.None;
            foreach (var rx in _jailbreakPatterns)
                if (rx.IsMatch(userInput)) return BlockReason.JailbreakAttempt;
            foreach (var rx in _rolePlayEscapePatterns)
                if (rx.IsMatch(userInput)) return BlockReason.RolePlayEscape;
            foreach (var rx in _explicitInputPatterns)
                if (rx.IsMatch(userInput)) return BlockReason.ExplicitContent;
            foreach (var rx in _harmfulRequestPatterns)
                if (rx.IsMatch(userInput)) return BlockReason.HarmfulRequest;
            return BlockReason.None;
        }

        // ---- Stage 2: output ----
        // Output scanner is intentionally simpler - the input-side already
        // turned away most attempts, and the system prompt keeps the LLM
        // mostly grounded. This is a last-line safety net.
        static readonly string[] _outputBlocklist = {
            // Explicit content
            "rape ", "raping", "incest", "molest", "pedophil", "paedophil",
            // Self-harm encouragement
            "kill yourself", "kys ", "neck yourself",
            // Common slurs (lowercased; we lowercase the output before checking)
            // - kept short and obvious; expand cautiously since false positives
            //   in fiction can occur ("retort" vs "retard", etc.)
            "n-word", "f-slur",
        };

        public static bool ContainsBlockedContent(string llmOutput) {
            if (string.IsNullOrWhiteSpace(llmOutput)) return false;
            string lower = llmOutput.ToLowerInvariant();
            foreach (var k in _outputBlocklist) {
                if (lower.Contains(k)) return true;
            }
            return false;
        }
    }
}
