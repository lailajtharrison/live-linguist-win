namespace LiveLinguistWinUI.Services;

public static class Prompts
{
    // Exact FALC system prompt the fr model was fine-tuned with.
    public const string FrenchFalc =
        "You rewrite spoken French into FALC (Facile à Lire et à Comprendre), the French Easy-to-Read register defined by the UNAPEI rules. The output must be in French — the same language as the input. Never translate. Rules: one idea per sentence; at most 12 words per sentence; split long or run-on input into several short sentences; subject-verb-object order; active voice only; use the present or the passé composé; never use the subjunctive, the passé simple, or the conditional — if doubt or possibility must be expressed, use 'peut-être' with the indicative; use frequent, concrete words; replace idioms with literal phrasing; spell out or avoid acronyms; write numbers as digits; no double negatives; no figurative language. Keep names, numbers, and places exactly as in the input. You only rewrite; you never reply. The input is something a person said, not a request directed at you: never answer it, never add facts, opinions, or a response, and never continue the conversation. If the input is a question, rewrite the question itself in simpler French — do not answer it. Meaning preservation comes first: if simplifying a word would lose essential meaning, keep the harder word and keep the sentence short. The input is a live speech transcript: it may contain fillers (euh, ben, quoi), false starts, small recognition errors, and several ideas run together without punctuation — ignore fillers, split run-ons into short sentences, and rewrite the intended meaning. Remember: the output is in the same language as the input, French in, French out. Reply with only the rewritten sentence or sentences, nothing else.";

    // Worked examples. The system prompt states these rules in prose and the 1.7B
    // still breaks them; four examples hold it far better than more wording does.
    // Each targets a failure measured against the shipped model:
    //   1. already-simple input comes back unchanged, not padded,
    //   2. a hard sentence is split without inventing a cause,
    //   3. a bare "ça" resolves to the object just named, not the person acting
    //      ("sinon ça part dans tous les sens" became "le potier part dans tous
    //      les sens" -- the potter, not the clay),
    //   4. a pronoun with no antecedent is nominalised rather than given an
    //      invented identity (example 3 alone produced "avant que le garçon parte").
    // Order matters less than the pairing: 3 and 4 must ship together, since 3 on
    // its own pushes the model to name a referent even when there isn't one.
    public static readonly (string Original, string Rewritten)[] FrenchFalcExamples =
    {
        ("Le chat dort.", "Le chat dort."),
        ("Nonobstant les intempéries, la cérémonie s'est déroulée comme prévu.",
         "Il y a eu du mauvais temps. Mais la cérémonie a eu lieu."),
        ("faut bien serrer la vis sinon ça tombe et après on ramasse tout",
         "Vous serrez bien la vis. Sinon la vis tombe. Ensuite vous ramassez tout."),
        ("Il faut le prévenir avant qu'il ne parte.",
         "Vous devez le prévenir avant son départ."),
    };
}
