using System.Text.RegularExpressions;

namespace FlightOpsHub.Core;

/// <summary>
/// Port of the ICAO-prefix / country / continent lookup tables and
/// categorization logic from the top of scenery_data.py (repo root, not
/// under backend/) - no filesystem dependency, pure lookup/regex logic.
/// </summary>
public static class SceneryCategorization
{
    public static readonly string[] ContinentOrder =
    {
        "europe", "north_america", "south_america", "asia", "africa", "oceania", "other",
    };

    // --- ICAO prefixes -> country (two-letter takes precedence over one-letter) ---
    public static readonly Dictionary<string, string> TwoLetterIcao = new()
    {
        // Europe
        ["EB"] = "Belgium", ["ED"] = "Germany", ["ET"] = "Germany", ["EE"] = "Estonia", ["EF"] = "Finland",
        ["EG"] = "United Kingdom", ["EH"] = "Netherlands", ["EI"] = "Ireland", ["EK"] = "Denmark",
        ["EL"] = "Luxembourg", ["EN"] = "Norway", ["EP"] = "Poland", ["ES"] = "Sweden", ["EV"] = "Latvia",
        ["EY"] = "Lithuania",
        ["LA"] = "Albania", ["LB"] = "Bulgaria", ["LC"] = "Cyprus", ["LD"] = "Croatia", ["LE"] = "Spain",
        ["LF"] = "France", ["LG"] = "Greece", ["LH"] = "Hungary", ["LI"] = "Italy", ["LJ"] = "Slovenia",
        ["LK"] = "Czech Republic", ["LL"] = "Israel", ["LM"] = "Malta", ["LN"] = "Monaco", ["LO"] = "Austria",
        ["LP"] = "Portugal", ["LQ"] = "Bosnia and Herzegovina", ["LR"] = "Romania", ["LS"] = "Switzerland",
        ["LT"] = "Turkey", ["LU"] = "Moldova", ["LW"] = "North Macedonia", ["LX"] = "Gibraltar",
        ["LY"] = "Serbia", ["LZ"] = "Slovakia",
        ["UK"] = "Ukraine", ["UM"] = "Belarus",
        ["BI"] = "Iceland", ["BG"] = "Greenland",
        ["GC"] = "Spain",

        // Russia / CIS / Caucasus / Central Asia
        ["UA"] = "Kazakhstan", ["UB"] = "Azerbaijan", ["UD"] = "Armenia", ["UG"] = "Georgia",
        ["UT"] = "Uzbekistan",
        ["UH"] = "Russia", ["UI"] = "Russia", ["UL"] = "Russia", ["UN"] = "Russia", ["UO"] = "Russia",
        ["UR"] = "Russia", ["US"] = "Russia", ["UU"] = "Russia", ["UW"] = "Russia",

        // Middle East
        ["OA"] = "Afghanistan", ["OB"] = "Bahrain", ["OE"] = "Saudi Arabia", ["OI"] = "Iran",
        ["OJ"] = "Jordan", ["OK"] = "Kuwait", ["OL"] = "Lebanon", ["OM"] = "United Arab Emirates",
        ["OO"] = "Oman", ["OP"] = "Pakistan", ["OR"] = "Iraq", ["OS"] = "Syria", ["OT"] = "Qatar",
        ["OY"] = "Yemen",

        // South / Southeast Asia
        ["VA"] = "India", ["VE"] = "India", ["VI"] = "India", ["VO"] = "India", ["VC"] = "Sri Lanka",
        ["VD"] = "Cambodia", ["VG"] = "Bangladesh", ["VH"] = "Hong Kong", ["VL"] = "Laos",
        ["VM"] = "Macau", ["VN"] = "Nepal", ["VQ"] = "Bhutan", ["VR"] = "Maldives", ["VT"] = "Thailand",
        ["VV"] = "Vietnam", ["VY"] = "Myanmar",
        ["WA"] = "Indonesia", ["WI"] = "Indonesia", ["WQ"] = "Indonesia", ["WR"] = "Indonesia",
        ["WB"] = "Malaysia", ["WM"] = "Malaysia", ["WP"] = "East Timor", ["WS"] = "Singapore",

        // East Asia
        ["RC"] = "Taiwan", ["RJ"] = "Japan", ["RO"] = "Japan", ["RK"] = "South Korea", ["RP"] = "Philippines",
        ["ZK"] = "North Korea", ["ZM"] = "Mongolia",

        // Africa
        ["DA"] = "Algeria", ["DB"] = "Benin", ["DF"] = "Burkina Faso", ["DG"] = "Ghana",
        ["DI"] = "Ivory Coast", ["DN"] = "Nigeria", ["DR"] = "Niger", ["DT"] = "Tunisia", ["DX"] = "Togo",
        ["GA"] = "Mali", ["GB"] = "Gambia", ["GF"] = "Sierra Leone", ["GG"] = "Guinea-Bissau",
        ["GL"] = "Liberia", ["GM"] = "Morocco", ["GO"] = "Senegal", ["GQ"] = "Mauritania",
        ["GU"] = "Guinea", ["GV"] = "Cape Verde",
        ["HA"] = "Ethiopia", ["HB"] = "Burundi", ["HC"] = "Somalia", ["HD"] = "Djibouti", ["HE"] = "Egypt",
        ["HH"] = "Eritrea", ["HK"] = "Kenya", ["HL"] = "Libya", ["HR"] = "Rwanda", ["HS"] = "Sudan",
        ["HT"] = "Tanzania", ["HU"] = "Uganda",
        ["FA"] = "South Africa", ["FB"] = "Botswana", ["FC"] = "Republic of the Congo",
        ["FD"] = "Eswatini", ["FE"] = "Central African Republic", ["FG"] = "Equatorial Guinea",
        ["FH"] = "Saint Helena", ["FI"] = "Mauritius", ["FK"] = "Cameroon", ["FL"] = "Zambia",
        ["FM"] = "Madagascar", ["FN"] = "Angola", ["FO"] = "Gabon", ["FP"] = "Sao Tome and Principe",
        ["FQ"] = "Mozambique", ["FS"] = "Seychelles", ["FT"] = "Chad", ["FV"] = "Zimbabwe",
        ["FW"] = "Malawi", ["FX"] = "Lesotho", ["FY"] = "Namibia", ["FZ"] = "DR Congo",

        // Oceania / Pacific
        ["NF"] = "Fiji", ["NG"] = "Kiribati", ["NI"] = "Niue", ["NL"] = "Wallis and Futuna",
        ["NS"] = "Samoa", ["NT"] = "French Polynesia", ["NV"] = "Vanuatu", ["NW"] = "New Caledonia",
        ["NZ"] = "New Zealand",
        ["AG"] = "Solomon Islands", ["AN"] = "Nauru", ["AY"] = "Papua New Guinea",

        // North America / Caribbean / Central America
        ["MM"] = "Mexico", ["MG"] = "Guatemala", ["MH"] = "Honduras", ["MN"] = "Nicaragua",
        ["MP"] = "Panama", ["MR"] = "Costa Rica", ["MS"] = "El Salvador", ["MZ"] = "Belize",
        ["MB"] = "Turks and Caicos", ["MD"] = "Dominican Republic", ["MK"] = "Jamaica",
        ["MT"] = "Haiti", ["MU"] = "Cuba", ["MW"] = "Cayman Islands", ["MY"] = "Bahamas",
        ["TA"] = "Antigua and Barbuda", ["TB"] = "Barbados", ["TD"] = "Dominica",
        ["TF"] = "French Antilles", ["TG"] = "Grenada", ["TI"] = "US Virgin Islands",
        ["TJ"] = "Puerto Rico", ["TK"] = "Saint Kitts and Nevis", ["TL"] = "Saint Lucia",
        ["TN"] = "Netherlands Antilles", ["TQ"] = "Anguilla", ["TR"] = "Montserrat",
        ["TT"] = "Trinidad and Tobago", ["TU"] = "British Virgin Islands", ["TV"] = "Saint Vincent",
        ["TX"] = "Bermuda",

        // South America
        ["SA"] = "Argentina", ["SB"] = "Brazil", ["SC"] = "Chile", ["SD"] = "Brazil", ["SE"] = "Ecuador",
        ["SF"] = "Chile", ["SG"] = "Paraguay", ["SI"] = "Brazil", ["SJ"] = "Brazil", ["SK"] = "Colombia",
        ["SL"] = "Bolivia", ["SM"] = "Suriname", ["SN"] = "Brazil", ["SO"] = "French Guiana",
        ["SP"] = "Peru", ["SS"] = "Brazil", ["SU"] = "Uruguay", ["SV"] = "Venezuela", ["SW"] = "Brazil",
        ["SY"] = "Guyana",

        // Alaska / US Pacific territories
        ["PA"] = "United States", ["PF"] = "United States", ["PO"] = "United States",
        ["PH"] = "United States", ["PG"] = "Guam", ["PK"] = "Marshall Islands", ["PT"] = "Micronesia",
        ["PW"] = "Palau", ["PC"] = "Kiribati",
    };

    public static readonly Dictionary<string, string> OneLetterIcao = new()
    {
        ["K"] = "United States",
        ["C"] = "Canada",
        ["Y"] = "Australia",
        ["Z"] = "China",
    };

    // Supplementary keywords for packages with no ICAO code in the name (best-effort).
    public static readonly Dictionary<string, string> KeywordToCountry = new()
    {
        ["france"] = "France", ["germany"] = "Germany", ["japan"] = "Japan",
        ["unitedstates"] = "United States", ["usa"] = "United States",
        ["britain"] = "United Kingdom", ["england"] = "United Kingdom", ["scotland"] = "United Kingdom",
        ["italy"] = "Italy", ["spain"] = "Spain", ["australia"] = "Australia", ["canada"] = "Canada",
        ["brazil"] = "Brazil", ["china"] = "China", ["india"] = "India", ["mexico"] = "Mexico",
        ["greece"] = "Greece", ["netherlands"] = "Netherlands", ["switzerland"] = "Switzerland",
        ["austria"] = "Austria", ["portugal"] = "Portugal", ["poland"] = "Poland",
        ["sweden"] = "Sweden", ["norway"] = "Norway", ["finland"] = "Finland", ["denmark"] = "Denmark",
        ["ireland"] = "Ireland", ["iceland"] = "Iceland", ["turkey"] = "Turkey", ["russia"] = "Russia",
        ["indonesia"] = "Indonesia", ["thailand"] = "Thailand", ["vietnam"] = "Vietnam",
        ["philippines"] = "Philippines", ["taiwan"] = "Taiwan", ["newzealand"] = "New Zealand",
    };

    public static readonly Dictionary<string, string> CountryToContinent = BuildCountryToContinent();

    private static Dictionary<string, string> BuildCountryToContinent()
    {
        var map = new Dictionary<string, string>();
        void Register(string[] countries, string continent)
        {
            foreach (var c in countries) map[c] = continent;
        }

        Register(new[]
        {
            "Germany", "United Kingdom", "Netherlands", "Ireland", "Denmark", "Luxembourg",
            "Norway", "Poland", "Sweden", "Latvia", "Lithuania", "Belgium", "Estonia",
            "Finland", "Albania", "Bulgaria", "Cyprus", "Croatia", "Spain", "France",
            "Greece", "Hungary", "Italy", "Slovenia", "Czech Republic", "Malta", "Monaco",
            "Austria", "Portugal", "Bosnia and Herzegovina", "Romania", "Switzerland",
            "Moldova", "North Macedonia", "Gibraltar", "Serbia", "Slovakia", "Ukraine",
            "Belarus", "Iceland",
        }, "europe");

        Register(new[]
        {
            "Kazakhstan", "Azerbaijan", "Armenia", "Georgia", "Uzbekistan", "Russia",
            "Turkey", "Israel", "Afghanistan", "Bahrain", "Saudi Arabia", "Iran", "Jordan",
            "Kuwait", "Lebanon", "United Arab Emirates", "Oman", "Pakistan", "Iraq", "Syria",
            "Qatar", "Yemen", "India", "Sri Lanka", "Cambodia", "Bangladesh", "Hong Kong",
            "Laos", "Macau", "Nepal", "Bhutan", "Maldives", "Thailand", "Vietnam", "Myanmar",
            "Indonesia", "Malaysia", "East Timor", "Singapore", "Taiwan", "Japan",
            "South Korea", "Philippines", "North Korea", "Mongolia", "China",
        }, "asia");

        Register(new[]
        {
            "Algeria", "Benin", "Burkina Faso", "Ghana", "Ivory Coast", "Nigeria", "Niger",
            "Tunisia", "Togo", "Mali", "Gambia", "Sierra Leone", "Guinea-Bissau", "Liberia",
            "Morocco", "Senegal", "Mauritania", "Guinea", "Cape Verde", "Ethiopia",
            "Somalia", "Djibouti", "Egypt", "Eritrea", "Kenya", "Libya", "Rwanda", "Sudan",
            "Tanzania", "Uganda", "Burundi", "South Africa", "Botswana",
            "Republic of the Congo", "Eswatini", "Central African Republic",
            "Equatorial Guinea", "Saint Helena", "Mauritius", "Cameroon", "Zambia",
            "Madagascar", "Angola", "Gabon", "Sao Tome and Principe", "Mozambique",
            "Seychelles", "Chad", "Zimbabwe", "Malawi", "Lesotho", "Namibia", "DR Congo",
        }, "africa");

        Register(new[]
        {
            "Fiji", "Kiribati", "Niue", "Wallis and Futuna", "Samoa", "French Polynesia",
            "Vanuatu", "New Caledonia", "New Zealand", "Solomon Islands", "Nauru",
            "Papua New Guinea", "Australia", "Guam", "Marshall Islands", "Micronesia",
            "Palau",
        }, "oceania");

        Register(new[]
        {
            "Mexico", "Guatemala", "Honduras", "Nicaragua", "Panama", "Costa Rica",
            "El Salvador", "Belize", "Turks and Caicos", "Dominican Republic", "Jamaica",
            "Haiti", "Cuba", "Cayman Islands", "Bahamas", "Antigua and Barbuda", "Barbados",
            "Dominica", "French Antilles", "Grenada", "US Virgin Islands", "Puerto Rico",
            "Saint Kitts and Nevis", "Saint Lucia", "Netherlands Antilles", "Anguilla",
            "Montserrat", "Trinidad and Tobago", "British Virgin Islands", "Saint Vincent",
            "Bermuda", "United States", "Canada", "Greenland",
        }, "north_america");

        Register(new[]
        {
            "Argentina", "Brazil", "Chile", "Ecuador", "Paraguay", "Colombia", "Bolivia",
            "Suriname", "French Guiana", "Peru", "Uruguay", "Venezuela", "Guyana",
        }, "south_america");

        return map;
    }

    private static readonly Regex IcaoTokenRegex = new(@"(?<![A-Za-z0-9])([A-Za-z]{4})(?![A-Za-z0-9])");

    private static readonly Dictionary<string, Regex> KeywordRegexCache = KeywordToCountry.Keys.ToDictionary(
        kw => kw,
        kw => new Regex(@"(?<![a-z])" + Regex.Escape(kw) + @"(?![a-z])"));

    private static string? LookupIcao(string token)
    {
        var two = token.Length >= 2 ? token[..2].ToUpperInvariant() : "";
        if (TwoLetterIcao.TryGetValue(two, out var country)) return country;
        var one = token.Length >= 1 ? token[..1].ToUpperInvariant() : "";
        return OneLetterIcao.TryGetValue(one, out var country1) ? country1 : null;
    }

    /// <summary>
    /// The first segment of a community package's folder name (before the
    /// first "-"/"_") is almost always a developer/studio abbreviation
    /// (e.g. "orbx-airport-...", "fsdg-airport-..."), not a place code -
    /// otherwise a four-letter abbreviation like "ORBX" or "FSDG" could get
    /// mistaken for an ICAO code (prefix "OR" = Iraq, "FS" = Seychelles).
    /// That segment is therefore skipped when scanning.
    /// </summary>
    private static List<string> IcaoTokensFromFolder(string? folderName)
    {
        if (string.IsNullOrEmpty(folderName)) return new List<string>();
        var parts = folderName.Split(new[] { '-', '_' }, 2);
        var remainder = parts.Length > 1 ? parts[1] : "";
        return IcaoTokenRegex.Matches(remainder).Select(m => m.Groups[1].Value.ToUpperInvariant()).ToList();
    }

    /// <summary>
    /// Titles only accept tokens written in ALL CAPS in the original text -
    /// real ICAO codes are written that way in titles, while an ordinary
    /// four-letter English word in "Title Case" would otherwise produce a
    /// false match.
    /// </summary>
    private static List<string> IcaoTokensFromTitle(string? title)
    {
        return IcaoTokenRegex.Matches(title ?? "")
            .Select(m => m.Groups[1].Value)
            .Where(t => t == t.ToUpperInvariant())
            .ToList();
    }

    public static string? FindIcaoCode(string? folderName, string? title)
    {
        foreach (var token in IcaoTokensFromFolder(folderName))
        {
            if (LookupIcao(token) != null) return token;
        }
        foreach (var token in IcaoTokensFromTitle(title))
        {
            if (LookupIcao(token) != null) return token;
        }
        return null;
    }

    public static (string? Country, string Continent) CategorizeScenery(string? folderName, string? title)
    {
        foreach (var token in IcaoTokensFromFolder(folderName).Concat(IcaoTokensFromTitle(title)))
        {
            var country = LookupIcao(token);
            if (country != null)
            {
                return (country, CountryToContinent.GetValueOrDefault(country, "other"));
            }
        }

        var combined = $"{folderName} {title}".ToLowerInvariant().Replace("-", "").Replace("_", "").Replace(" ", "");
        var combinedSpaced = $"{folderName} {title}".ToLowerInvariant();
        foreach (var (keyword, country) in KeywordToCountry)
        {
            var pattern = KeywordRegexCache[keyword];
            if (pattern.IsMatch(combinedSpaced) || combined.Contains(keyword))
            {
                return (country, CountryToContinent.GetValueOrDefault(country, "other"));
            }
        }

        return (null, "other");
    }

    public static int ContinentSortKey(string continent)
    {
        var index = Array.IndexOf(ContinentOrder, continent);
        return index >= 0 ? index : ContinentOrder.Length;
    }
}
