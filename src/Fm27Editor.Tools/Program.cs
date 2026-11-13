using System.Diagnostics;
using Fm27Editor.Core.Discovery;
using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Competitions;
using Fm27Editor.Core.ForceAccept;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.MassEdit;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Nations;
using Fm27Editor.Core.Officials;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;
using Fm27Editor.Core.Transfer;
using Fm27Editor.Core.Verification;

// Football Manager 27 Editor command-line tools.
//   players [name]                     list players (filtered by name)
//   verify <golden.json>               compare decoded players with values copied from the game UI
//   set <name> <field> <value>         write one attribute / CA / PA / position / personality value,
//                                      Height (cm) or Born (yyyy-MM-dd)
//   find-offsets <golden.json> <out>   rebuild the offset profile for the running build
//   transfer dump <name> [out.json]    snapshot player/contract/pending/club/finance bytes
//   transfer peek <name>               print the transfer-relevant fields of one player
//   transfer diff <before> <after>     compare two snapshots by object offset
//   captain <id> [vice] [--restore 0x<person>] [--write]   set/undo team captain or vice captain
//   clubvision <club> <type> <importance> [--from <old>] [--write]   set a club vision importance
//   maxattrs <id> [--write]            team preset: every performance attribute 20
//   compinfo <id>                      read one competition's editable fields
//   compset <id> <subs|fg|bg|trim|wb|wbstart|wbend|rep> <value>   write one competition field
//   compsnap | compdiff <path>         baseline the whole competition list and diff it after an FMRTE Save
//   keepplaying <id>|batch <ids...> [--write]   remove a planned retirement
//   nationtv <nation>                  the 20 transfer-value tiers (men and women); nationtv <nation> set <men|women> <i> <v> [--write]
//   referees [name]                    the game's referees, and refset <id|name> <field> <value> [--write]
//   globals [n]                        clubs anchor and the global arrays derived from it (read-only)//   gamecheck [feature]                read every feature on the attached build; gamewrites runs the write smoke test
//   loans [name]                       every player the game has out on loan
//   loan show|recall <id> [--write]    inspect or recall a loan; loan set <id> wage=|started=|expires=|signed=|
//                                      agreed=|buyback=|monthly=|unused= [--write] edits the loan contract
string offsetsDir = Environment.GetEnvironmentVariable("FM27_OFFSETS") ?? Path.Combine(AppContext.BaseDirectory, "offsets");
Console.OutputEncoding = System.Text.Encoding.UTF8;
try
{
    return args.FirstOrDefault() switch
    {
        "status" => Status(),
        "accelerate" => AccelerateCli.Run(args[1..], Attach),
        "golden" => GoldenCli.Run(args[1..], Attach),
        "players" => Players(args.Length > 1 ? string.Join(' ', args[1..]) : null),
        "healthplayers" => HealthPlayers(),
        "healthstructure" => HealthStructure(),
        "healthstaff" => HealthStaff(),
        "healthclubs" => HealthClubs(),
            "healthprofile" => HealthProfileCommand(),
            "healthodd" => HealthOddCommand(),
        "exportplayers" => ExportPlayers(args[1], args[2], args[3..].Select(int.Parse).ToArray()),
        "freezecheck" => FreezeCheck(int.Parse(args[1]), Enum.Parse<PlayerAttribute>(args[2], true), int.Parse(args[3])),
        "freezedecreasecheck" => FreezeDecreaseCheck(int.Parse(args[1]), Enum.Parse<PlayerAttribute>(args[2], true)),
        "freezedecreasereconnect" => FreezeDecreaseReconnect(args[1], int.Parse(args[2]), Enum.Parse<PlayerAttribute>(args[3], true)),
        "freezebalancecheck" => FreezeBalanceCheck(int.Parse(args[1]), long.Parse(args[2])),
        "freezestorecheck" => FreezeStoreCheck(args[1], int.Parse(args[2]), int.Parse(args[3])),
        "freezeruncheck" => FreezeRunCheck(args[1], int.Parse(args[2]), int.Parse(args[3])),
        "masscheck" => MassCheck(Enum.Parse<PlayerAttribute>(args[1], true), int.Parse(args[2]), args[3..].Select(int.Parse).ToArray()),
        "massmulticheck" => MassMultiCheck(int.Parse(args[1])),
        "presetbatchcheck" => PresetBatchCheck(Enum.Parse<PlayerPreset>(args[1], true), args[2..].Select(int.Parse).ToArray()),
        "injuryundocheck" => InjuryUndoCheck(int.Parse(args[1])),
        "squadhealcheck" => SquadHealCheck(int.Parse(args[1])),
        "massbench" => MassBench(args.Length > 1 ? int.Parse(args[1]) : 50000, args.Length > 2 ? int.Parse(args[2]) : 1),
        "massscalarcheck" => MassScalarCheck(Enum.Parse<PlayerField>(args[1], true), Enum.Parse<MassEditMode>(args[2], true), long.Parse(args[3]), args[4..].Select(int.Parse).ToArray()),
        "massqualitycheck" => MassQualityCheck(Enum.Parse<PlayerQuality>(args[1], true), Enum.Parse<MassEditMode>(args[2], true), int.Parse(args[3]), args[4..].Select(int.Parse).ToArray()),
        "massratingcheck" => MassRatingCheck(args[1], args[2], Enum.Parse<MassEditMode>(args[3], true), int.Parse(args[4]), args[5..].Select(int.Parse).ToArray()),
        "staffmasscheck" => StaffMassCheck(Enum.Parse<StaffMassField>(args[1], true), Enum.Parse<MassEditMode>(args[2], true), int.Parse(args[3]), args[4..].Select(int.Parse).ToArray()),
        "staffattrmasscheck" => StaffAttributeMassCheck(Enum.Parse<StaffAttribute>(args[1], true), Enum.Parse<MassEditMode>(args[2], true), int.Parse(args[3]), args[4..].Select(int.Parse).ToArray()),
        "staffpersonmasscheck" => StaffPersonalityMassCheck(Enum.Parse<StaffPersonality>(args[1], true), Enum.Parse<MassEditMode>(args[2], true), int.Parse(args[3]), args[4..].Select(int.Parse).ToArray()),
        "clubmasscheck" => ClubMassCheck(Enum.Parse<ClubField>(args[1], true), Enum.Parse<MassEditMode>(args[2], true), long.Parse(args[3]), args[4..].Select(int.Parse).ToArray()),
        "retirements" => Retirements(args[1..].Select(int.Parse).ToArray()),
        "keepplaying" => KeepPlaying(int.Parse(args[1]), args.Contains("--write")),
        "keepplayingbatch" => KeepPlayingBatch(args[1..]),
        "forceaccept" => ForceAcceptCommand(args[1..]),
        "forceaccept-hook" => ForceAcceptHookCommand(args[1..]),
        "forceacceptdeal" => ForceAcceptDealCommand(args[1..]),
        "forceacceptoffer" => ForceAcceptOfferCommand(args[1..]),
        "captain" => CaptainCommand(args[1..]),
        "maxattrs" => MaxAttributesCommand(int.Parse(args[1]), args.Contains("--write")),
        "masswagecheck" => MassWageCheck(int.Parse(args[1]), args[2..].Select(int.Parse).ToArray()),
        "staffwagemass" => StaffWageMassCommand(int.Parse(args[1]), args[2..]),
        "unhundo" => UnhUndoCheck(int.Parse(args[1])),
        "unhids" => UnhappyIds(args.Length > 1 ? string.Join(' ', args[1..]) : null),
        "banids" => BannedIds(),
        "banundo" => BanUndoCheck(int.Parse(args[1])),
        "setattrid" => SetAttributeById(int.Parse(args[1]), Enum.Parse<PlayerAttribute>(args[2], true), int.Parse(args[3])),
        "setwageid" => SetWageById(int.Parse(args[1]), int.Parse(args[2])),
        "contractid" => ContractById(int.Parse(args[1])),
        "currentloan" => CurrentLoanCommand(int.Parse(args[1])),
        "loans" => LoansCommand(args.Length > 1 ? string.Join(' ', args[1..]) : null),
        "loanraw" => LoanRawCommand(int.Parse(args[1])),
        "loanstate" => LoanStateCommand(),
        "allowprobe" => AllowProbeCommand(int.Parse(args[1])),
        "swap" => SwapCommand(args[1], args[2], args.Contains("--write")),
        "compare" => CompareCommand(args[1], args[2]),
        "globals" => GlobalsCommand(args.Length > 1 ? int.Parse(args[1]) : 3),
        "globalscan" => GlobalScanCommand(),
        "allowdeep" => AllowDeepCommand(int.Parse(args[1])),
        "board" => BoardCommand(string.Join(' ', args[1..])),
        "allowhuman" => AllowHumanCommand(),
        "allowat" => AllowAtCommand(Convert.ToUInt64(args[1].Replace("0x", ""), 16)),
        "nationtv" => NationTvCommand(args[1..]),
        "referees" => RefereesCommand(args.Length > 1 ? string.Join(' ', args[1..]) : null),
        "refset" => RefereeSetCommand(args[1], args[2], int.Parse(args[3]), args.Contains("--write")),
        "refsurvey" => RefereeSurveyCommand(),
        "loan" => LoanCommand(args[1..]),
        "verify" => Verify(args[1]),
        "set" => Set(args[1], args[2], args[3]),
        "move" => Move(args[1], int.Parse(args[2]), int.Parse(args[3]), args.Contains("--write")),
        "releaseinspect" => ReleaseInspect(int.Parse(args[1])),
        "release" => ReleasePlayer(int.Parse(args[1]), args.Contains("--write")),
        "contractrm" => args.Length >= 3 ? ContractRemove(args[1], int.Parse(args[2])) : Usage(),
            "contract" => ContractCommand(args[1], args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null),
        "find-offsets" => FindOffsets(args[1], args[2], args.SkipWhile(a => a != "--date").Skip(1).FirstOrDefault()),
        "transfer" => Transfer(args[1..]),
        "clauses" => args[1] == "fee" ? SellOnsWithFee() : Clauses(int.Parse(args[1])),
        "income" => IncomeCommand(int.Parse(args[1]), args[2..]),
        "alloc" => AllocCommand(int.Parse(args[1])),
        "clubdebt" => ClubDebtCommand(args[1..]),
        "visionprobe" => VisionProbeCommand(int.Parse(args[1])),
        "stadiumprobe" => StadiumProbeCommand(),
        "visiondump" => VisionDumpCommand(int.Parse(args[1]), args.Length > 2 ? int.Parse(args[2]) : 3),
        "visionobj" => VisionObjCommand(int.Parse(args[1]), int.Parse(args[2])),
        "visionadd" => VisionAddCommand(int.Parse(args[1]), args[2..]),
        "regprobe" => RegProbeCommand(args[1..]),
        "staffallow" => StaffAllowCommand(int.Parse(args[1]), args[2..]),
        "msccscan" => MsccScanCommand(int.Parse(args[1])),
        "dealsprobe" => DealsProbeCommand(int.Parse(args[1])),
        "dealscan" => DealScanCommand(int.Parse(args[1])),
        "vtscan" => VtScanCommand(args[1..]),
        "findvt" => FindVtCommand(args[1..]),
        "nearrange" => NearRangeCommand(args[1..]),
        "scanpat" => ScanPatternCommand(args[1..]),
        "compvec" => CompVecCommand(args[1..]),
        "compvec2" => CompVec2Command(args[1..]),
        "objstr" => ObjStrCommand(args[1..]),
        "dumpmod" => DumpModuleCommand(args[1..]),
        "regunlock" => RegUnlockCommand(args[1..]),
        "msccfind" => MsccFindCommand(args[1..]),
        "deals" => DealsCommand(args[1..]),
        "dealfee" => DealFeeCommand(args[1..]),
        "visionitems" => VisionItemsCommand(args[1..]),
        "dealstatus" => DealStatusCommand(args[1..]),
        "visionraw" => VisionRawCommand(int.Parse(args[1]), args[2..]),
        "visionstatus" => VisionStatusCommand(),
        "visionscan" => VisionScanCommand(),
        "vision" => VisionCommand(int.Parse(args[1]), args[2..]),
        "club" => ClubInfoCommand(int.Parse(args[1])),
        "financeprobe" => FinanceProbeCommand(int.Parse(args[1])),
        "clubfields" => ClubFieldsCommand(int.Parse(args[1])),
        "squad" => SquadCommand(int.Parse(args[1])),
        "heal" => HealCommand(string.Join(" ", args[1..])),
        "injuries" => InjuriesCommand(string.Join(" ", args[1..])),
        "bans" => BansCommand(string.Join(" ", args[1..])),
        "permitprobe" => PermitProbeCommand(),
        "nearptr" => NearPtrCommand(Convert.ToUInt64(args[1], 16), args[2..]),
        "reltype" => RelTypeCommand(Convert.ToInt32(args[1], 16)),
        "cities" => CitiesCommand(string.Join(" ", args[1..])),
        "playersborn" => PlayersBornCommand(int.Parse(args[1])),
        "playerscontract" => PlayersContractCommand(args[1], args[2], args[3..]),
        "playersclause" => PlayersClauseCommand(args[1], args[2], args[3..]),
        "playerscontinent" => PlayersContinentCommand(string.Join(' ', args[1..])),
        "playersnation" => PlayersNationCommand(int.Parse(args[1]), args.Contains("--include-other")),
        "playerstrained" => PlayersTrainedCommand(args[1], int.Parse(args[2])),
        "playerswonderkids" => PlayersWonderkidsCommand(),
        "careersurvey" => CareerSurvey(),
        "offerscan" => OfferScan(),
        "staffcontract" => StaffContractCommand(string.Join(" ", args[1..])),
        "callups" => CallUpsCommand(string.Join(" ", args[1..])),
        "ctypes" => ContractTypes(),
        "future" => FutureCommand(string.Join(" ", args[1..])),
        "future-create" => FutureCreateCommand(args[1..]),
        "untrained" => UntrainedCommand(args[1..]),
        "proposalsurvey" => ProposalSurvey(args[1..]),
            "proposalsurveyraw" => ProposalSurveyRaw(),
        "offerlist" => OfferList(args.Length > 1 ? string.Join(" ", args[1..]) : null),
        "careers" => CareersCommand(string.Join(" ", args[1..])),
        "careerprobe" => CareerProbe(string.Join(" ", args[1..])),
        "staffjobdept" => StaffJobDepartments(),
        "relsurvey" => RelSurvey(),
        "relplayer" => RelPlayer(int.Parse(args[1])),
        "pfhist" => PlayerFieldHistogram(Enum.Parse<PlayerField>(args[1]), args.Length > 2 ? int.Parse(args[2]) : 0),
        "personidx" => PersonIndexCommand(Convert.ToUInt64(args[1], 16)),
        "near32" => Near32Command(int.Parse(args[1]), args[2..]),
        "neardate" => NearDateCommand(int.Parse(args[1]), int.Parse(args[2]), args[3..]),
        "vecscan" => VecScanCommand(Convert.ToUInt64(args[1], 16), Convert.ToInt32(args[2], 16)),
        "languages" => LanguagesCommand(string.Join(" ", args[1..])),
        "languagevalue" => LanguageValueCommand(int.Parse(args[1]), Convert.ToUInt64(args[2], 16), int.Parse(args[3])),
        "banmatches" => BanMatchesCommand(int.Parse(args[1]), Convert.ToUInt64(args[2], 16), int.Parse(args[3]), int.Parse(args[4])),
        "injuryduration" => InjuryDurationCommand(int.Parse(args[1]), Convert.ToUInt64(args[2], 16), int.Parse(args[3]), int.Parse(args[4])),
        "destroy" => DestroyCommand(string.Join(" ", args[1..])),
        "removebans" => RemoveBansCommand(string.Join(" ", args[1..])),
        "setplayer" => SetPlayerField(string.Join(" ", args[1..^2]), args[^2], long.Parse(args[^1])),
        "statuses" => StatusesCommand(string.Join(" ", args[1..])),
        "unh" => UnhCommand(string.Join(" ", args[1..])),
        "ptrvt" => PtrVtCommand(Convert.ToUInt64(args[1], 16), Convert.ToInt32(args[2], 16)),
        "nation" => args.Length >= 4 && long.TryParse(args[^1], out long nv) && !args[^2].All(char.IsDigit)
            ? NationCommand(string.Join(" ", args[1..^2]), args[^2], nv) : NationCommand(string.Join(" ", args[1..]), null, null),
            "export" => ExportCommand(args[1], args[2]),
            "restore" => RestoreCommand(args[1], args[2]),
            "tactics" => TacticsCommand(args[1]),
            "stadiumcolour" => StadiumColourCommand(args[1..]),
            "stadium" => args.Length >= 4 && long.TryParse(args[^1], out long sv) && Enum.TryParse<Fm27Editor.Core.Stadiums.StadiumField>(args[^2], true, out _) ? StadiumCommand(string.Join(" ", args[1..^2]), args[^2], sv) : StadiumCommand(string.Join(" ", args[1..]), null, null),
        "poke" => PokeCommand(Convert.ToUInt64(args[1], 16), args[2], args[3]),
        "rootvecs" => RootVecsCommand(Convert.ToUInt64(args[1], 16)),
        "vecbounds" => VecBoundsCommand(Convert.ToUInt64(args[1], 16), Convert.ToUInt64(args[2], 16)),
        "near" => NearCommand(int.Parse(args[1]), int.Parse(args[2])),
        "clubprobe" => ClubProbeCommand(args[1], args[2], int.Parse(args[3]), args[4..]),
        "nationbytes" => NationBytesCommand(Convert.ToInt32(args[1], 16), int.Parse(args[2]), args[3..]),
        "nationprobe" =>NationProbeCommand(args[1], args[2], int.Parse(args[3]), args[4..]),
        "setstaff" =>SetStaffCommand(string.Join(" ", args[1..^2]), args[^2], int.Parse(args[^1])),
        "staff" => StaffCommand(string.Join(" ", args[1..])),
        "staffid" => StaffById(args[1..].Select(int.Parse).ToArray()),
        "staffmove" => StaffMove(int.Parse(args[1]), int.Parse(args[2]), args.Contains("--write"), args.SkipWhile(a => a != "--job").Skip(1).Select(a => (byte?)byte.Parse(a)).FirstOrDefault(), args.SkipWhile(a => a != "--wage").Skip(1).Select(a => (int?)int.Parse(a)).FirstOrDefault()),
        "staffmovesurvey" => StaffMoveSurvey(int.Parse(args[1])),
        "setstaffabilityid" => SetStaffAbilityById(int.Parse(args[1]), int.Parse(args[2]), int.Parse(args[3])),
        "staffcontractid" => StaffContractById(int.Parse(args[1]), null),
        "staffrenew" => StaffRenewCommand(int.Parse(args[1]), args.Length > 2 ? int.Parse(args[2]) : 4),
        "staffexpire" => StaffExpireCommand(int.Parse(args[1])),
        "setstaffwageid" => StaffContractById(int.Parse(args[1]), int.Parse(args[2])),
        "staffcareers" => StaffCareers(),
        "staffknowledge" => StaffKnowledgeCommand(string.Join(" ", args[1..])),
        "tendencysurvey" => TendencySurvey(),
        "staffprobe" =>StaffProbeCommand(int.Parse(args[1])),
        "vthist" =>VtHistCommand(Convert.ToUInt64(args[1], 16), Convert.ToInt32(args[2], 16)),
        "findlist" =>FindListCommand(Convert.ToUInt64(args[1], 16)),
        "hex" =>HexCommand(Convert.ToUInt64(args[1], 16), Convert.ToInt32(args[2], 16)),
        "findperson" =>FindPersonCommand(args[1..].Select(int.Parse).ToArray()),
        "pokedate" =>PokeDateCommand(string.Join(" ", args[1..^3]), args[^3], Convert.ToInt32(args[^2], 16), DateOnly.Parse(args[^1])),
        "datediff" =>DateDiffCommand(args[1], Convert.ToInt32(args[2], 16), args[3], Convert.ToInt32(args[4], 16)),
        "match" =>MatchCommand(args[1], int.Parse(args[2]), args[3..]),
        "removeunh" =>RemoveUnhCommand(string.Join(" ", args[1..])),
        "byid" =>ById(args[1..].Select(int.Parse).ToArray()),
        "peek" => Peek(args[1], Convert.ToInt32(args[2], 16), int.Parse(args[3]), args[4..].Select(int.Parse).ToArray()),
        "setclub" => SetClubField(int.Parse(args[1]), args[2], long.Parse(args[3])),
        "clublists" => ClubListsCommand(int.Parse(args[1])),
        "clubvision" => ClubVisionCommand(args[1..]),
        "addclubvis" => ClubVisAddCommand(int.Parse(args[1]), int.Parse(args[2])),
        "addclubaff" => ClubAffAddCommand(int.Parse(args[1]), int.Parse(args[2])),
        "delclubaff" => ClubAffDeleteCommand(int.Parse(args[1]), int.Parse(args[2])),
        "delclubvis" => ClubVisDeleteCommand(int.Parse(args[1]), int.Parse(args[2])),
        "clubfinancelists" => ClubFinanceListsCommand(int.Parse(args[1])),
            "clubsponsor" => ClubSponsorCommand(args[1..]),
        "clubstaff" => ClubStaffCommand(int.Parse(args[1])),
        "comps" => CompsCommand(string.Join(" ", args[1..])),
        "compviaclubs" => CompViaClubsCommand(args[1..]),
        "preset" => args.Length > 1 && args[1] is "heal" or "inspire" or "destroy" or "allinone" or "show" ? PresetRunCommand(args[1..]) : PresetCommand(args[1..]),
        "goldengen" => GoldenGenCommand(args[1..]),
        "register" => RegisterCommand(args[1..]),
        "youth" => YouthCommand(args[1..]),
        "compdiag" => CompDiagCommand(args[1..]),
        "compobj" => CompObjCommand(int.Parse(args[1])),
        "comproot" => CompRootCommand(args[1..]),
        "comproot3" => CompRoot3Command(args[1..]),
        "compbase" => CompBaseCommand(args[1..]),
        "compref" => CompRefCommand(),
        "compfind" => CompFindCommand(args[1..]),
        "hexdump" => HexDumpCommand(args[1..]),
        "compinfo" => CompInfoCommand(int.Parse(args[1])),
        "compprize" => CompPrizeCommand(args[1..]),
        "compstandings" => CompStandingsCommand(args[1..]),
        "compvar" => CompVarCommand(args[1..]),
        "comprules" => CompRulesCommand(args[1..]),
        "compset" => CompSetCommand(args[1..]),
        "awards" => AwardsCommand(string.Join(" ", args[1..])),
        "nationcap" => NationCapCommand(string.Join(" ", args[1..])),
        "nationteams" => NationTeamsCommand(string.Join(" ", args[1..])),
        "nationkits" => NationKitsCommand(string.Join(" ", args[1..])),
        "fam" => FamiliarityCommand(args[1..]),
        "nationsquad" => NationSquadCommand(string.Join(" ", args[1..])),
        "nationstaff" => NationStaffCommand(string.Join(" ", args[1..])),
        "natcontract" => NationContractCommand(args[1..]),
        "natboth" => NationBothCommand(),
        "clubnames" => ClubNamesCommand(string.Join(" ", args[1..])),
        "injurytype" => InjuryTypeCommand(args[1..]),
        "injurydump" => InjuryDumpCommand(args[1..]),
        "injuryscan" => InjuryScanCommand(),
        "injuryadd" => InjuryAddCommand(args[1..]),
        "stadiumdump" => StadiumDumpCommand(args[1..]),
        "reldump" => RelDumpCommand(string.Join(" ", args[1..])),
        "relstats" => RelStatsCommand(),
        "relsample" => RelTypeSampleCommand(args[1..]),
        "heapfind" => HeapFindCommand(args[1..]),
        "playerslot" => PlayerSlotCommand(args[1], args.Length > 2 ? args[2] : null),
        "workpermit" => WorkPermitCommand(args[1..]),
        "permits" => PermitsCommand(args[1..]),
        "permitbatch" => PermitBatchCommand(args[1..]),
        "citydump" => CityDumpCommand(args[1]),
        "citylink" => CityLinkCommand(args[1..]),
        "cityfield" => CityFieldCommand(args[1..]),
        "ptrpath" => PtrPathCommand(args[1..]),
        "modptr" => ModPtrCommand(args[1..]),
        "playerptr" => PlayerPtrCommand(args[1..]),
        "ptactics" => PlayerTacticsCommand(args[1..]),
        "squadtactics" => SquadTacticsCommand(args[1..]),
        "vtsurvey" => VtSurveyCommand(),
        "heapvectors" => HeapVectorsCommand(Convert.ToUInt32(args[1].Replace("0x", ""), 16), int.Parse(args[2])),
        "tablewalk" => TableWalkCommand(Convert.ToUInt32(args[1].Replace("0x", ""), 16), args.Length > 2 ? Convert.ToUInt32(args[2].Replace("0x", ""), 16) : 0),
        "vectorsof" => VectorsOfCommand(Convert.ToUInt32(args[1].Replace("0x", ""), 16), int.Parse(args[2])),
        "nationnames" => NationNamesCommand(),
        "future-remove" => FutureRemoveCommand(int.Parse(args[1]), args.Length > 2 && args[2] == "--write"),
        "futureaddr" => FutureAddrCommand(args.Length > 1 ? int.Parse(args[1]) : 0),
        "gamewrites" or "steamwrites" => SteamWrites.Run(Attach(writable: true)),
        "calibrate" => Calibration.Run(args[1..]),
        "stadiumowner" => StadiumOwnerCommand(string.Join(' ', args[1..])),
        "agrsurvey" => AgrSurveyCommand(),
        "slotsbyname" => SlotsByNameCommand(args[1]),
        "chunkslot" => ChunkSlotCommand(Convert.ToUInt64(args[1].Replace("0x", ""), 16)),
        "agentchunk" => AgentChunkCommand(Convert.ToUInt64(args[1].Replace("0x", ""), 16)),
        "personsof" => PersonsOfCommand(Convert.ToUInt32(args[1].Replace("0x", ""), 16), args.Length > 2 ? int.Parse(args[2]) : 5),
        "gamecheck" or "steamcheck" => RunGameCheck(args),
        "vectorroot" => VectorRootCommand(Convert.ToUInt64(args[1].Replace("0x", ""), 16), Convert.ToInt32(args[2].Replace("0x", ""), 16)),
        "promisescan" => PromiseScanCommand(),
        "compidx" => CompIdxCommand(args[1..].Select(int.Parse).ToArray()),
        "findclub" => FindClubCommand(string.Join(' ', args[1..])),
        "savewatch" => SaveWatchCommand(args.Length > 1 && int.TryParse(args[1], out int watchSeconds) ? watchSeconds : 60),
        "promises" => PromisesCommand(args[1..]),
        "temporarypermits" => TemporaryPermitsCommand(),
        "permitscan" => PermitScanCommand(args.Length > 1 ? int.Parse(args[1]) : 1),
        "memdump" => MemDumpCommand(args[1], args.Length > 2 ? Convert.ToInt32(args[2], 16) : 0x80),
        "intwithdrawal" => IntWithdrawalCommand(string.Join(" ", args[1..])),
        "stadiumset" => StadiumSetCommand(args[1..]),
        "nationkititem" => NationKitItemCommand(args[1..]),
        "nationcolours" => NationColoursCommand(args[1..]),
        "nationfind" => NationFindCommand(args[1], int.Parse(args[2]), args[3..]),
        "natpref" => NatPrefCommand(args[1..]),
        "nationagr" => NationAgrCommand(args[1..], null),
        "nationregions" => NationRegionsCommand(),
        "nationagrempty" => NationAgrEmptyCommand(args[1..]),
        "nationcoeff" => NationCoeffCommand(string.Join(" ", args[1..])),
        "natrivals" => NatRivalsCommand(string.Join(" ", args[1..])),
        "termstats" => TermStatsCommand(),
        "minusstats" => MinusStatsCommand(),
        "nationcont" => NationContCommand(string.Join(" ", args[1..]), null),
        "setnationcont" => NationContCommand(string.Join(" ", args[1..^5]), args[^5..]),
        "nationdeep" => NationDeepCommand(args[1], args[2], int.Parse(args[3]), int.Parse(args[4]), int.Parse(args[5]), args[6..]),
        "compsurvey" => CompSurveyCommand(Convert.ToInt32(args[1], 16), int.Parse(args[2])),
        "compscan" => CompScanCommand(Convert.ToInt32(args[1], 16), int.Parse(args[2])),
        "compsnap" => CompSnapCommand(args[1]),
        "compdiff" => CompDiffCommand(args[1]),
        "clubdeep" => ClubDeepCommand(int.Parse(args[1]), int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4]), int.Parse(args[5]), args[6..]),
        "clubrel" => ClubRelCommand(int.Parse(args[1]), null),
        "addclubrel" => ClubRelAddCommand(int.Parse(args[1]), int.Parse(args[2]), args.Length > 3 ? int.Parse(args[3]) : 50),
        "delclubrel" => ClubRelDeleteCommand(int.Parse(args[1]), int.Parse(args[2])),
        "setclubrel" => ClubRelCommand(int.Parse(args[1]), args[2..]),
        "probe" => Probe(Convert.ToUInt64(args[1], 16), Convert.ToInt32(args[2], 16), int.Parse(args[3]), args[4..]),
        _ => Usage(),
    };
}
catch (Exception ex) when (ex is GameNotRunningException or UnsupportedBuildException or StaleObjectException or DiscoveryException or MemoryAccessException or ArgumentOutOfRangeException)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

int Usage()
{
    Console.Error.WriteLine("usage: players [name] | verify <golden.json> | set <name> <field> <value> | find-offsets <golden.json> <out.json> [--date yyyy-MM-dd] | transfer dump|peek <name> [out.json] | transfer diff <before.json> <after.json>");
    return 1;
}

GameSession Attach(bool writable) => GameSession.Attach(GameSession.LoadProfiles(offsetsDir), writable);

// healthprofile: read a sample of every module with the loaded profile and print PASS/FAIL per module.
// healthodd: list the players behind the healthprofile players/contracts near-misses (first 400 records).
int HealthOddCommand()
{
    using var session = Attach(writable: false);
    var profile = session.Profile;
    var all = session.Players.LoadAll();
    var sample = all.Take(400).ToList();
    Console.WriteLine($"first {sample.Count} players of {all.Count:N0}");
    foreach (var p in sample)
    {
        var name = p.DisplayName;
        bool printable = !string.IsNullOrWhiteSpace(name) && name.Length <= 80 && name.All(c => c >= ' ' && c != '\u007f');
        if (!printable)
        {
            var raw = session.Memory.Read(p.Address + (ulong)profile.Fields.FullName, 16);
            Console.WriteLine($"odd name  id {p.Id} at 0x{p.Address:X} bytes {Convert.ToHexString(raw)}");
        }
    }
    if (profile.Contract is { } offsets)
    {
        int bad = 0;
        foreach (var p in sample)
        {
            ulong contract = session.Memory.ReadUInt64(p.Address + (ulong)offsets.PlayerContract);
            if (contract == 0) continue;
            ulong vtable = session.Memory.ReadUInt64(contract);
            if (vtable == session.ModuleBase + offsets.ContractVtableRva) continue;
            if (++bad > 30) continue;
            Console.WriteLine($"odd contract id {p.Id} '{p.DisplayName}' contract 0x{contract:X} vt 0x{vtable:X} (want 0x{session.ModuleBase + offsets.ContractVtableRva:X})");
        }
        Console.WriteLine($"{bad} contract mismatches");
    }
    return 0;
}
int HealthProfileCommand()
{
    using var session = Attach(writable: false);
    var results = new Fm27Editor.Core.Offsets.ProfileHealth(session.Memory, session.Profile, session.ModuleBase).Run();
    foreach (var r in results) Console.WriteLine($"{(r.Ok ? "PASS" : "FAIL")} {r.Module,-14} {r.Detail}");
    int failed = results.Count(r => !r.Ok);
    Console.WriteLine(failed == 0 ? "profile ok" : $"{failed} module(s) failed");
    return failed == 0 ? 0 : 1;
}
int HealthPlayers()
{
    using var session = Attach(writable: false);
    var all = session.Players.LoadAll();
    int count = Math.Min(2000, all.Count);
    var sample = Enumerable.Range(0, count).Select(i => all[(int)((long)i * all.Count / count)]).ToArray();
    int positionsInRange = sample.Count(p => p.Positions.All(v => v <= 20));
    int natural = sample.Count(p => p.Positions.Contains((byte)20));
    int attributesInRange = sample.Count(p => p.RawAttributes.All(v => v <= 100));
    int personalitiesInRange = sample.Count(p => p.Personality.All(v => v <= 20));
    PlayerAttribute[] goalkeeper = [PlayerAttribute.AerialReach, PlayerAttribute.CommandOfArea, PlayerAttribute.Communication,
        PlayerAttribute.Handling, PlayerAttribute.Kicking, PlayerAttribute.OneOnOnes, PlayerAttribute.TendencyToPunch,
        PlayerAttribute.Reflexes, PlayerAttribute.RushingOut];
    var keepers = sample.Where(p => p.Position(PlayerPosition.Goalkeeper) == 20).ToArray();
    var outfield = sample.Where(p => p.Position(PlayerPosition.Goalkeeper) <= 5).ToArray();
    var strikers = sample.Where(p => p.Position(PlayerPosition.Striker) == 20 && p.Position(PlayerPosition.DefenderCentral) <= 10).ToArray();
    var defenders = sample.Where(p => p.Position(PlayerPosition.DefenderCentral) == 20 && p.Position(PlayerPosition.Striker) <= 10).ToArray();
    static double? Mean(PlayerRecord[] group, Func<PlayerRecord, double> value) => group.Length == 0 ? null : group.Average(value);
    double? gkGap = Mean(keepers, p => goalkeeper.Average(a => (double)p.Raw(a))) - Mean(outfield, p => goalkeeper.Average(a => (double)p.Raw(a)));
    double? finishingGap = Mean(strikers, p => p.Raw(PlayerAttribute.Finishing)) - Mean(defenders, p => p.Raw(PlayerAttribute.Finishing));
    double? tacklingGap = Mean(defenders, p => p.Raw(PlayerAttribute.Tackling)) - Mean(strikers, p => p.Raw(PlayerAttribute.Tackling));
    bool enough = count >= 50;
    bool positionsOk = enough && positionsInRange >= count * .99 && natural >= count * .95;
    bool attributesOk = enough && attributesInRange >= count * .99 && gkGap >= 20 && finishingGap >= 20 && tacklingGap >= 20;
    bool personalityOk = enough && personalitiesInRange >= count * .99;
    Console.WriteLine($"build {session.Profile.Build}; sampled {count} of {all.Count}; read-only diagnostics");
    Console.WriteLine($"positions {(positionsOk ? "pass" : "check")}: range {positionsInRange}/{count}, natural {natural}/{count}");
    Console.WriteLine($"attributes {(attributesOk ? "pass" : "check")}: range {attributesInRange}/{count}; goalkeeper gap {gkGap:F1}, finishing gap {finishingGap:F1}, tackling gap {tacklingGap:F1}");
    Console.WriteLine($"groups: goalkeepers {keepers.Length}, outfield {outfield.Length}, strikers {strikers.Length}, defenders {defenders.Length}");
    Console.WriteLine($"personality {(personalityOk ? "pass" : "check")}: range {personalitiesInRange}/{count}");
    return 0;
}

int HealthStructure()
{
    using var session = Attach(writable: false);
    var profile = session.Profile;
    var contract = profile.Contract ?? throw new InvalidOperationException("Profile has no contract offsets");
    var team = profile.Team ?? throw new InvalidOperationException("Profile has no team offsets");
    var club = profile.Club ?? throw new InvalidOperationException("Profile has no club offsets");
    int ownerOffset = contract.Owner ?? throw new InvalidOperationException("Profile has no contract owner offset");
    int personOffset = profile.Relations?.PlayerPerson ?? throw new InvalidOperationException("Profile has no player person offset");
    var all = session.Players.LoadAll();
    int count = Math.Min(2000, all.Count);
    var sample = Enumerable.Range(0, count).Select(i => all[(int)((long)i * all.Count / count)]).ToArray();
    bool Pointer(ulong address, out ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        bool ok = session.Memory.TryRead(address, bytes);
        value = ok ? BitConverter.ToUInt64(bytes) : 0;
        return ok;
    }
    int contractCount = 0, contractFits = 0, teamCount = 0, teamFits = 0, unreadable = 0;
    var mismatches = new List<string>();
    foreach (var player in sample)
    {
        if (!Pointer(player.Address + (ulong)contract.PlayerContract, out ulong contractAddress)) { unreadable++; continue; }
        if (contractAddress != 0)
        {
            contractCount++;
            bool vtRead = Pointer(contractAddress, out ulong vt);
            bool ownerRead = Pointer(contractAddress + (ulong)ownerOffset, out ulong owner);
            if (vtRead && vt == session.ModuleBase + contract.ContractVtableRva && ownerRead && owner == player.Address + (ulong)personOffset) contractFits++;
            else if (mismatches.Count < 10)
                mismatches.Add($"  id {player.Id}: vtable {(vtRead ? $"0x{vt:X}" : "unreadable")}, owner {(ownerRead ? $"0x{owner:X}" : "unreadable")}, expected owner 0x{player.Address + (ulong)personOffset:X}");
        }
        if (!Pointer(player.Address + (ulong)team.PlayerTeam, out ulong teamAddress)) { unreadable++; continue; }
        if (teamAddress == 0) continue;
        teamCount++;
        if (!Pointer(teamAddress, out ulong teamVt) || teamVt != session.ModuleBase + team.VtableRva ||
            !Pointer(teamAddress + (ulong)contract.TeamClub, out ulong clubAddress) || clubAddress == 0 ||
            !Pointer(clubAddress, out ulong clubVt) || clubVt != session.ModuleBase + club.ClubVtableRva ||
            !Pointer(teamAddress + (ulong)team.Squad, out ulong begin) || !Pointer(teamAddress + (ulong)team.Squad + 8, out ulong end) ||
            begin == 0 || end < begin || (end - begin) % 8 != 0 || end - begin > 8 * 5000) continue;
        var bytes = new byte[(int)(end - begin)];
        if (!session.Memory.TryRead(begin, bytes)) continue;
        for (int i = 0; i < bytes.Length; i += 8)
            if (BitConverter.ToUInt64(bytes, i) == player.Address) { teamFits++; break; }
    }
    bool contractsOk = contractCount >= 50 && contractFits >= .95 * contractCount && unreadable == 0;
    bool teamsOk = teamCount >= 50 && teamFits >= .95 * teamCount && unreadable == 0;
    Console.WriteLine($"build {profile.Build}; sampled {count} of {all.Count}; read-only diagnostics");
    Console.WriteLine($"contracts {(contractsOk ? "pass" : "check")}: owner and vtable {contractFits}/{contractCount}");
    foreach (var mismatch in mismatches) Console.WriteLine(mismatch);
    Console.WriteLine($"teams {(teamsOk ? "pass" : "check")}: club and squad membership {teamFits}/{teamCount}; unreadable player pointers {unreadable}");
    return 0;
}

int HealthStaff()
{
    using var session = Attach(writable: false);
    var profile = session.Profile;
    var offsets = profile.Staff ?? throw new InvalidOperationException("Profile has no staff offsets");
    var contract = profile.Contract ?? throw new InvalidOperationException("Profile has no contract offsets");
    var all = session.Staff.LoadAll();
    int count = Math.Min(2000, all.Count);
    var sample = Enumerable.Range(0, count).Select(i => all[(int)((long)i * all.Count / count)]).ToArray();
    var keeper = new List<byte>(); var coachGk = new List<byte>(); var coachPhysio = new List<byte>();
    var physio = new List<byte>(); var physioJudge = new List<byte>(); var scout = new List<byte>();
    var otherTypes = new Dictionary<(ulong Vtable, short? Job), int>();
    int valuesFit = 0, contractCount = 0, contractFit = 0, ownerFit = 0, otherVtable = 0, unreadable = 0;
    bool Pointer(ulong address, out ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        bool ok = session.Memory.TryRead(address, bytes);
        value = ok ? BitConverter.ToUInt64(bytes) : 0;
        return ok;
    }
    foreach (var person in sample)
    {
        if (person.CurrentAbility is >= 0 and <= 200 && person.PotentialAbility is >= -200 and <= 200 &&
            person.HomeReputation is >= 0 and <= 10000 && person.CurrentReputation is >= 0 and <= 10000 &&
            person.WorldReputation is >= 0 and <= 10000 && person.Attributes.All(v => v <= 20) && person.Personality.All(v => v <= 20)) valuesFit++;
        if (!Pointer(person.Address + (ulong)offsets.Job, out ulong job)) { unreadable++; continue; }
        if (job == 0) continue;
        contractCount++;
        bool vtRead = Pointer(job, out ulong vt);
        bool ownerRead = Pointer(job + (ulong)offsets.JobPerson, out ulong owner);
        if (ownerRead && owner == person.Address) ownerFit++;
        if (vtRead && vt != session.ModuleBase + contract.ContractVtableRva && ownerRead && owner == person.Address)
        {
            otherVtable++;
            var key = (vt, person.JobCode);
            otherTypes[key] = otherTypes.GetValueOrDefault(key) + 1;
        }
        if (!vtRead || vt != session.ModuleBase + contract.ContractVtableRva || !ownerRead || owner != person.Address) continue;
        contractFit++;
        if (person.JobCode is not { } code) continue;
        int role = code > 1 && code % 2 == 1 ? code - 1 : code;
        var attributes = new byte[3];
        if (!session.Memory.TryRead(person.StaffAddress + (ulong)offsets.Attributes[StaffAttribute.CoachingGoalkeeping].Offset, attributes.AsSpan(0, 1)) ||
            !session.Memory.TryRead(person.StaffAddress + (ulong)offsets.Attributes[StaffAttribute.Physiotherapy].Offset, attributes.AsSpan(1, 1)) ||
            !session.Memory.TryRead(person.StaffAddress + (ulong)offsets.Attributes[StaffAttribute.JudgingPlayerPotential].Offset, attributes.AsSpan(2, 1))) { unreadable++; continue; }
        if (role == 34) keeper.Add(attributes[0]);
        if (role is 2 or 20) { coachGk.Add(attributes[0]); coachPhysio.Add(attributes[1]); }
        if (role is 12 or 50) { physio.Add(attributes[1]); physioJudge.Add(attributes[2]); }
        if (role is 14 or 44) scout.Add(attributes[2]);
    }
    static double? Gap(List<byte> high, List<byte> low) => high.Count == 0 || low.Count == 0 ? null : high.Average(v => (double)v) - low.Average(v => (double)v);
    double? gk = Gap(keeper, coachGk), medical = Gap(physio, coachPhysio), judging = Gap(scout, physioJudge);
    bool fits = count >= 50 && valuesFit >= .97 * count && contractCount >= 50 && contractFit >= .95 * contractCount &&
        gk >= 20 && medical >= 20 && judging >= 20 && unreadable == 0;
    Console.WriteLine($"build {profile.Build}; sampled {count} of {all.Count}; read-only diagnostics");
    Console.WriteLine($"staff {(fits ? "pass" : "check")}: values {valuesFit}/{count}, contracts {contractFit}/{contractCount}, owner {ownerFit}/{contractCount}, other vtable with matching owner {otherVtable}, unreadable {unreadable}");
    foreach (var entry in otherTypes.OrderByDescending(e => e.Value).Take(10))
        Console.WriteLine($"  other job vtable 0x{entry.Key.Vtable:X}, job {entry.Key.Job}: {entry.Value}");
    Console.WriteLine($"goalkeeping gap {gk:F1}, physiotherapy gap {medical:F1}, judging gap {judging:F1}");
    Console.WriteLine($"groups: goalkeeper coaches {keeper.Count}, coaches {coachGk.Count}, physios {physio.Count}, scouts {scout.Count}");
    return 0;
}

int HealthClubs()
{
    using var session = Attach(writable: false);
    var profile = session.Profile;
    var co = profile.Club ?? throw new InvalidOperationException("Profile has no club offsets");
    var team = profile.Team ?? throw new InvalidOperationException("Profile has no team offsets");
    var contract = profile.Contract ?? throw new InvalidOperationException("Profile has no contract offsets");
    int teamsOffset = co.Lists?.Teams ?? throw new InvalidOperationException("Profile has no club teams offset");
    var all = session.LoadClubs(session.Players.LoadAll());
    int count = Math.Min(2000, all.Count);
    var sample = Enumerable.Range(0, count).Select(i => all[(int)((long)i * all.Count / count)]).ToArray();
    bool Pointer(ulong address, out ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        bool ok = session.Memory.TryRead(address, bytes);
        value = ok ? BitConverter.ToUInt64(bytes) : 0;
        return ok;
    }
    int fit = 0, missing = 0;
    foreach (var club in sample)
    {
        if (!Pointer(club.Address + (ulong)teamsOffset, out ulong begin) ||
            !Pointer(club.Address + (ulong)teamsOffset + 8, out ulong end) || begin == 0 || end <= begin ||
            (end - begin) % 8 != 0 || end - begin > 8 * 128) { missing++; continue; }
        if (!Pointer(begin, out ulong senior) || senior == 0 || !Pointer(senior, out ulong vt) ||
            vt != session.ModuleBase + team.VtableRva || !Pointer(senior + (ulong)contract.TeamClub, out ulong owner) || owner != club.Address) continue;
        long? reputation = session.ClubFields.Read(senior, ClubField.TeamReputation);
        if (club.Id is > 0 && reputation is >= 0 and <= 10000) fit++;
    }
    bool ok = count >= 50 && fit >= .95 * count;
    Console.WriteLine($"build {profile.Build}; sampled {count} of {all.Count} clubs reachable from players; read-only diagnostics");
    Console.WriteLine($"clubs {(ok ? "pass" : "check")}: senior team/owner/id/reputation {fit}/{count}; invalid or empty team list {missing}");
    return 0;
}

int BannedIds()
{
    using var session = Attach(writable: false);
    int shown = 0;
    foreach (var p in session.Players.LoadAll())
    {
        int count = session.ReadBans(p).Count(BanWriter.IsMatchBan);
        if (count == 0) continue;
        Console.WriteLine($"{p.Id} {p.DisplayName}: {count} match bans");
        if (++shown == 10) break;
    }
    return 0;
}

int BanUndoCheck(int id)
{
    using var session = Attach(writable: true);
    var player = session.Players.LoadAll().Single(p => p.Id == id);
    var writer = new BanWriter(session.Memory, session.Profile, session.ModuleBase);
    var plan = writer.PlanRemoveMatchBans(player) ?? throw new InvalidOperationException("No match bans");
    Console.WriteLine($"{id}: {plan.OldCount} records, {plan.OldCount - plan.NewCount} match bans");
    writer.ApplyRemoval(plan);
    Console.WriteLine($"after lift: {session.ReadBans(player).Count} records");
    writer.UndoRemoval(plan);
    int restored = session.ReadBans(player).Count;
    Console.WriteLine($"after undo: {restored} records");
    if (restored != plan.OldCount) throw new StaleObjectException("Ban list did not restore");
    return 0;
}

int UnhappyIds(string? club)
{
    using var session = Attach(writable: false);
    var date = session.ReadGameDate() ?? throw new InvalidOperationException("Game date unavailable");
    int shown = 0;
    foreach (var p in session.Players.LoadAll())
    {
        if (club is not null && !(p.ClubName?.Contains(club, StringComparison.OrdinalIgnoreCase) ?? false)) continue;
        var active = session.ReadUnhappiness(p).Count(x => x.IsActive(date));
        if (active == 0) continue;
        Console.WriteLine($"{p.Id} {p.DisplayName}: {active} active");
        shown++;
    }
    Console.WriteLine($"{shown} unhappy players");
    return 0;
}

// unhundo <player id>: remove active unhappiness and undo it on a test save.
int UnhUndoCheck(int id)
{
    using var session = Attach(writable: true);
    var player = session.Players.LoadAll().Single(p => p.Id == id);
    var date = session.ReadGameDate() ?? throw new InvalidOperationException("Game date unavailable");
    var writer = new UnhappinessWriter(session.Memory, session.Profile, session.ModuleBase);
    var plan = writer.PlanRemoveActive(player, date) ?? throw new InvalidOperationException("No active unhappiness");
    Console.WriteLine($"{id}: {plan.OldCount} records, {plan.OldCount - plan.NewCount} active");
    writer.ApplyRemoval(plan);
    Console.WriteLine($"after removal: {session.ReadUnhappiness(player).Count} records");
    writer.UndoRemoval(plan);
    int restored = session.ReadUnhappiness(player).Count;
    Console.WriteLine($"after undo: {restored} records");
    if (restored != plan.OldCount) throw new StaleObjectException("Unhappiness did not restore");
    return 0;
}

// squadhealcheck <club id>: plan, apply and undo the first team's Heal preset.
int MassWageCheck(int change, int[] ids)
{
    using var session = Attach(writable: true);
    var all = session.Players.LoadAll();
    var players = ids.Select(id => all.Single(p => p.Id == id)).ToArray();
    var plan = PlayerWageMassEdit.Plan(players, MassEditMode.Change, change);
    foreach (var item in plan) Console.WriteLine($"{item.Player.Id}: {item.Before} -> {item.After}");
    foreach (var item in plan) PlayerWageMassEdit.Apply(item, session.Players, session.Contracts);
    foreach (var item in plan.Reverse()) PlayerWageMassEdit.Undo(item, session.Players, session.Contracts);
    foreach (var item in plan)
        if (session.Players.Reload(item.Player.Address)?.Contract?.Wage != item.Before) throw new StaleObjectException("Wage did not restore");
    Console.WriteLine($"{plan.Count} wages applied, undone, and read back");
    return 0;
}

int StaffWageMassCommand(int delta, string[] args)
{
    bool write = args.Contains("--write");
    var ids = args.Where(a => a != "--write").Select(int.Parse).ToArray();
    using var session = Attach(writable: write);
    var staff = session.Staff.LoadAll();
    var targets = ids.Select(id => staff.Single(s => s.Id == id)).ToArray();
    var plan = StaffWageMassEdit.Plan(targets, session.StaffContracts, MassEditMode.Change, delta);
    foreach (var change in plan.Changes) Console.WriteLine($"{change.Staff.Id}: {change.Before} -> {change.After}");
    Console.WriteLine($"{plan.Changes.Count} changes, {plan.Unavailable} unavailable");
    if (!write) return 0;
    foreach (var change in plan.Changes) StaffWageMassEdit.Apply(change, session.Staff, session.StaffContracts);
    foreach (var change in plan.Changes)
    {
        var person = session.Staff.Reload(change.Staff.Address) ?? throw new StaleObjectException("Staff no longer exists");
        var contract = session.StaffContracts.Read(person) ?? throw new StaleObjectException("Contract no longer exists");
        if (contract.Wage != change.After) throw new StaleObjectException("Wage readback differs");
        Console.WriteLine($"{person.Id}: read back {contract.Wage}");
    }
    return 0;
}

int Retirements(int[] ids)
{
    using var session = Attach(writable: false);
    var reader = new PlannedRetirements(session.Memory, session.Profile, session.ModuleBase);
    var plans = reader.ReadAll();
    var wanted = ids.ToHashSet();
    var players = session.Players.LoadAll().Where(p => p.Id is { } id && (wanted.Count == 0 || wanted.Contains(id))).ToArray();
    Console.WriteLine($"{plans.Count} planned retirements");
    foreach (var player in players)
    {
        uint person = reader.PersonId(player);
        if (plans.TryGetValue(person, out var plan))
            Console.WriteLine($"{player.Id} {player.DisplayName}: {plan.Date:yyyy-MM-dd} (person {person})");
        else if (wanted.Count > 0) Console.WriteLine($"{player.Id} {player.DisplayName}: none (person {person})");
    }
    return 0;
}

int ClubMassCheck(ClubField field, MassEditMode mode, long value, int[] ids)
{
    using var session = Attach(writable: true);
    var clubs = session.LoadClubs(session.Players.LoadAll());
    var targets = ids.Select(id => clubs.Single(c => c.Id == id)).ToArray();
    var plan = ClubMassEdit.Plan(targets, field, mode, value, int.MinValue, int.MaxValue, session.Memory, session.Profile, session.ModuleBase);
    foreach (var change in plan) Console.WriteLine($"{change.Club.Id} {change.Field}: {change.Before} -> {change.After}");
    foreach (var change in plan) ClubMassEdit.Apply(change, session.Memory, session.Profile, session.ModuleBase);
    foreach (var change in plan.Reverse()) ClubMassEdit.Undo(change, session.Memory, session.Profile, session.ModuleBase);
    foreach (var change in plan)
        if (session.ClubFields.Read(change.Club.Address, field) != change.Before) throw new StaleObjectException("Club field did not restore");
    Console.WriteLine($"{plan.Count} changes applied, undone, and read back");
    return 0;
}

int StaffPersonalityMassCheck(StaffPersonality field, MassEditMode mode, int value, int[] ids)
{
    using var session = Attach(writable: true);
    var staff = session.Staff.LoadAll();
    var targets = ids.Select(id => staff.Single(s => s.Id == id)).ToArray();
    var plan = StaffPersonalityMassEdit.Plan(targets, field, mode, value);
    foreach (var change in plan) Console.WriteLine($"{change.Staff.Id} {change.Field}: {change.Before} -> {change.After}");
    foreach (var change in plan) StaffPersonalityMassEdit.Apply(change, session.Staff, session.StaffWriter);
    foreach (var change in plan.Reverse()) StaffPersonalityMassEdit.Undo(change, session.Staff, session.StaffWriter);
    foreach (var change in plan)
    {
        var current = session.Staff.Reload(change.Staff.Address) ?? throw new StaleObjectException("Staff member disappeared");
        if (current.Personality[(int)field] != change.Before) throw new StaleObjectException("Personality did not restore");
    }
    Console.WriteLine($"{plan.Count} changes applied, undone, and read back");
    return 0;
}

int StaffAttributeMassCheck(StaffAttribute field, MassEditMode mode, int value, int[] ids)
{
    using var session = Attach(writable: true);
    var staff = session.Staff.LoadAll();
    var targets = ids.Select(id => staff.Single(s => s.Id == id)).ToArray();
    var plan = StaffAttributeMassEdit.Plan(targets, field, mode, value, session.StaffWriter);
    foreach (var change in plan) Console.WriteLine($"{change.Staff.Id} {change.Field}: {change.Before} ({change.BeforeRaw}) -> {change.After} ({change.AfterRaw})");
    foreach (var change in plan) StaffAttributeMassEdit.Apply(change, session.Staff, session.StaffWriter);
    foreach (var change in plan.Reverse()) StaffAttributeMassEdit.Undo(change, session.Staff, session.StaffWriter);
    foreach (var change in plan)
    {
        var current = session.Staff.Reload(change.Staff.Address) ?? throw new StaleObjectException("Staff member disappeared");
        if (session.StaffWriter.ReadAttributeRaw(current, change.Field) != change.BeforeRaw) throw new StaleObjectException("Attribute did not restore");
    }
    Console.WriteLine($"{plan.Count} changes applied, undone, and exact bytes read back");
    return 0;
}

int StaffMassCheck(StaffMassField field, MassEditMode mode, int value, int[] ids)
{
    using var session = Attach(writable: true);
    var staff = session.Staff.LoadAll();
    var targets = ids.Select(id => staff.Single(s => s.Id == id)).ToArray();
    var plan = StaffMassEdit.Plan(targets, field, mode, value);
    foreach (var change in plan) Console.WriteLine($"{change.Staff.Id} {change.Field}: {change.Before} -> {change.After}");
    foreach (var change in plan) StaffMassEdit.Apply(change, session.Staff, session.StaffWriter);
    foreach (var change in plan.Reverse()) StaffMassEdit.Undo(change, session.Staff, session.StaffWriter);
    foreach (var change in plan)
    {
        var current = session.Staff.Reload(change.Staff.Address) ?? throw new StaleObjectException("Staff member disappeared");
        if (StaffMassEdit.Plan([current], change.Field, MassEditMode.Set, change.Before).Count != 0)
            throw new StaleObjectException("Field did not restore");
    }
    Console.WriteLine($"{plan.Count} changes applied, undone, and read back");
    return 0;
}

int MassRatingCheck(string kind, string fieldName, MassEditMode mode, int value, int[] ids)
{
    var field = kind switch
    {
        "position" => new PlayerRatingField(Position: Enum.Parse<PlayerPosition>(fieldName, true)),
        "personality" => new PlayerRatingField(Personality: Enum.Parse<PersonalityAttribute>(fieldName, true)),
        _ => throw new ArgumentException("Expected position or personality")
    };
    using var session = Attach(writable: true);
    var players = session.Players.LoadAll();
    var targets = ids.Select(id => players.Single(p => p.Id == id)).ToArray();
    var plan = PlayerRatingMassEdit.Plan(targets, field, mode, value);
    foreach (var change in plan) Console.WriteLine($"{change.Player.Id} {change.Field}: {change.Before} -> {change.After}");
    foreach (var change in plan) PlayerRatingMassEdit.Apply(change, session.Players, session.Writer);
    foreach (var change in plan.Reverse()) PlayerRatingMassEdit.Undo(change, session.Players, session.Writer);
    foreach (var change in plan)
    {
        var current = session.Players.Reload(change.Player.Address) ?? throw new StaleObjectException("Player disappeared");
        if (PlayerRatingMassEdit.Plan([current], change.Field, MassEditMode.Set, change.Before).Count != 0)
            throw new StaleObjectException("Rating did not restore");
    }
    Console.WriteLine($"{plan.Count} changes applied, undone, and read back");
    return 0;
}

int MassQualityCheck(PlayerQuality field, MassEditMode mode, int value, int[] ids)
{
    using var session = Attach(writable: true);
    var players = session.Players.LoadAll();
    var targets = ids.Select(id => players.Single(p => p.Id == id)).ToArray();
    var plan = PlayerQualityMassEdit.Plan(targets, field, mode, value);
    foreach (var change in plan) Console.WriteLine($"{change.Player.Id} {change.Field}: {change.Before} -> {change.After}");
    foreach (var change in plan) PlayerQualityMassEdit.Apply(change, session.Players, session.Writer);
    foreach (var change in plan.Reverse()) PlayerQualityMassEdit.Undo(change, session.Players, session.Writer);
    foreach (var change in plan)
    {
        var current = session.Players.Reload(change.Player.Address) ?? throw new StaleObjectException("Player disappeared");
        if (PlayerQualityMassEdit.Plan([current], change.Field, MassEditMode.Set, change.Before).Count != 0)
            throw new StaleObjectException("Field did not restore");
    }
    Console.WriteLine($"{plan.Count} changes applied, undone, and read back");
    return 0;
}

int MassBench(int count, int fieldCount)
{
    using var session = Attach(writable: false);
    var players = session.Players.LoadAll().Take(Math.Max(1, count)).ToArray();
    var access = session.PlayerFields;
    var defs = new (PlayerField Field, long Min, long Max)[]
    {
        (PlayerField.Morale, 1, 20),
        (PlayerField.Condition, 5000, 10000),
        (PlayerField.MatchSharpness, 5000, 10000),
    };
    var specs = defs.Take(Math.Clamp(fieldCount, 1, defs.Length)).Select(def => new MassFieldSpec<PlayerRecord>(
        (ushort)def.Field, def.Field.ToString(),
        player => access.Read(player, def.Field),
        (player, before, after) => access.Set(player, def.Field, before, after),
        1, MassEditMode.Change, def.Min, def.Max,
        value => access.ValidateValue(def.Field, value))).ToArray();

    var process = Process.GetCurrentProcess();
    long managedBefore = GC.GetTotalMemory(true);
    long workingBefore = process.WorkingSet64;
    var stopwatch = Stopwatch.StartNew();
    var progress = new Progress<MassProgress>(p =>
    {
        if (p.Done == p.Total || p.Done % 10000 == 0)
            Console.WriteLine($"{p.Phase}: {p.Done:N0}/{p.Total:N0}");
    });
    var plan = new MassPlanner().PlanAsync(players, specs, i => players[i].DisplayName, progress, CancellationToken.None)
        .GetAwaiter().GetResult();
    stopwatch.Stop();
    long managedAfter = GC.GetTotalMemory(false);
    long workingAfter = process.WorkingSet64;
    Console.WriteLine($"targets={players.Length:N0} fields={specs.Length} rows={plan.Rows:N0} changes={plan.ChangesCount:N0} refused={plan.RefusedCount:N0}");
    Console.WriteLine($"plan={stopwatch.ElapsedMilliseconds:N0} ms managedDelta={(managedAfter - managedBefore) / 1024.0 / 1024.0:N1} MB workingSetDelta={(workingAfter - workingBefore) / 1024.0 / 1024.0:N1} MB");
    return 0;
}
int MassScalarCheck(PlayerField field, MassEditMode mode, long value, int[] ids)
{
    using var session = Attach(writable: true);
    var players = session.Players.LoadAll();
    var targets = ids.Select(id => players.Single(p => p.Id == id)).ToArray();
    var plan = PlayerScalarMassEdit.Plan(targets, session.PlayerFields, field, mode, value);
    foreach (var change in plan) Console.WriteLine($"{change.Player.Id} {change.Field}: {change.Before} -> {change.After}");
    foreach (var change in plan) PlayerPresetBatch.Apply(change, session.Players, session.PlayerFields);
    foreach (var change in plan.Reverse()) PlayerPresetBatch.Undo(change, session.Players, session.PlayerFields);
    foreach (var change in plan)
        if (session.PlayerFields.Read(change.Player, change.Field) != change.Before) throw new StaleObjectException("Scalar did not restore");
    Console.WriteLine($"{plan.Count} changes applied, undone, and read back");
    return 0;
}

int SquadHealCheck(int clubId)
{
    using var session = Attach(writable: true);
    var club = session.LoadClubs(session.Players.LoadAll()).Single(c => c.Id == clubId);
    var team = club.FirstTeam ?? throw new InvalidOperationException("No first team");
    var squad = new SquadReader(session.Memory, session.Profile, session.ModuleBase).Read(team.Address);
    var writer = new InjuryWriter(session.Memory, session.Profile, session.ModuleBase);
    var injuries = squad.Select(writer.PlanRemoveAll).Where(p => p is not null).Cast<InjuryRemovalPlan>().ToArray();
    var fitness = PlayerPresetBatch.Plan(squad, session.PlayerFields, PlayerPreset.Fitness);
    Console.WriteLine($"{club.DisplayName}: {squad.Count} players, {injuries.Length} injury lists, {fitness.Count} fitness fields");
    foreach (var injury in injuries) writer.ApplyRemoval(injury);
    foreach (var c in fitness) PlayerPresetBatch.Apply(c, session.Players, session.PlayerFields);
    foreach (var c in fitness.Reverse()) PlayerPresetBatch.Undo(c, session.Players, session.PlayerFields);
    foreach (var injury in injuries.Reverse()) writer.UndoRemoval(injury);
    foreach (var c in fitness)
        if (session.PlayerFields.Read(c.Player, c.Field) != c.Before) throw new StaleObjectException("Fitness did not restore");
    foreach (var injury in injuries)
        if (session.ReadInjuries(injury.Player).Count != injury.Injuries.Count) throw new StaleObjectException("Injuries did not restore");
    Console.WriteLine("Heal applied and undone; all changed values read back");
    return 0;
}

// injuryundocheck <player id>: remove injuries and restore them for a test save.
int InjuryUndoCheck(int id)
{
    using var session = Attach(writable: true);
    var player = session.Players.LoadAll().Single(p => p.Id == id);
    var writer = new InjuryWriter(session.Memory, session.Profile, session.ModuleBase);
    var plan = writer.PlanRemoveAll(player) ?? throw new InvalidOperationException("Player has no injuries");
    Console.WriteLine($"{id}: {plan.Injuries.Count} injuries before");
    writer.ApplyRemoval(plan);
    Console.WriteLine($"after heal: {session.ReadInjuries(player).Count}");
    writer.UndoRemoval(plan);
    int restored = session.ReadInjuries(player).Count;
    Console.WriteLine($"after undo: {restored}");
    if (restored != plan.Injuries.Count) throw new StaleObjectException("Injuries did not restore");
    return 0;
}

// presetbatchcheck <fitness|morale> <player ids...>
int PresetBatchCheck(PlayerPreset preset, int[] ids)
{
    using var session = Attach(writable: true);
    var all = session.Players.LoadAll();
    var players = ids.Select(id => all.Single(p => p.Id == id)).ToArray();
    var plan = PlayerPresetBatch.Plan(players, session.PlayerFields, preset);
    foreach (var c in plan) Console.WriteLine($"{c.Player.Id} {c.Field}: {c.Before} -> {c.After}");
    foreach (var c in plan) PlayerPresetBatch.Apply(c, session.Players, session.PlayerFields);
    foreach (var c in plan.Reverse()) PlayerPresetBatch.Undo(c, session.Players, session.PlayerFields);
    foreach (var c in plan)
    {
        var actual = session.PlayerFields.Read(c.Player, c.Field);
        if (actual != c.Before) throw new StaleObjectException($"Undo failed for {c.Player.Id} {c.Field}");
    }
    Console.WriteLine($"Applied and undone {plan.Count} changes; all values read back");
    return 0;
}

// massmulticheck <player id>: exercise a two-field batch and undo on a test save.
int MassMultiCheck(int id)
{
    using var session = Attach(writable: true);
    var player = session.Players.LoadAll().Single(p => p.Id == id);
    var plan = PlayerMassEdit.Plan([player], [new(PlayerAttribute.Dribbling, MassEditMode.Change, -1), new(PlayerAttribute.Pace, MassEditMode.Change, -1)]);
    foreach (var c in plan) Console.WriteLine($"{id} {c.Attribute}: {c.Before} -> {c.After}");
    foreach (var c in plan) PlayerMassEdit.Apply(c, session.Players, session.Writer);
    foreach (var c in plan.Reverse()) PlayerMassEdit.Undo(c, session.Players, session.Writer);
    var after = session.Players.Reload(player.Address)!;
    foreach (var c in plan)
    {
        Console.WriteLine($"{c.Attribute}: raw {after.Raw(c.Attribute)}, expected {c.BeforeRaw}");
        if (after.Raw(c.Attribute) != c.BeforeRaw) throw new StaleObjectException("Batch undo did not restore original value");
    }
    return 0;
}

// masscheck <attribute> <value> <player ids...> previews, applies and undoes one batch.
int MassCheck(PlayerAttribute attribute, int value, int[] ids)
{
    using var session = Attach(writable: true);
    var all = session.Players.LoadAll();
    var selected = ids.Select(id => all.Single(p => p.Id == id)).ToArray();
    var plan = PlayerMassEdit.Plan(selected, attribute, MassEditMode.Set, value);
    foreach (var change in plan) Console.WriteLine($"{change.Player.Id} {change.Player.DisplayName}: {change.Before} -> {change.After}");
    foreach (var change in plan) PlayerMassEdit.Apply(change, session.Players, session.Writer);
    foreach (var change in plan.Reverse()) PlayerMassEdit.Undo(change, session.Players, session.Writer);
    foreach (var change in plan)
    {
        var after = session.Players.Reload(change.Player.Address)!;
        Console.WriteLine($"{after.Id} readback raw {after.Raw(attribute)} expected {change.BeforeRaw}");
        if (after.Raw(attribute) != change.BeforeRaw) throw new StaleObjectException("Batch undo did not restore original value");
    }
    return 0;
}

// freezecheck <player id> <attribute> <temporary displayed value>: exercise one freeze cycle on a test save.
int FreezeDecreaseReconnect(string path, int id, PlayerAttribute attribute)
{
    byte improved;
    using (var session = Attach(writable: true))
    {
        var player = session.Players.LoadAll().Single(p => p.Id == id);
        byte initial = player.Raw(attribute);
        if (initial is < 6 or > 95) throw new InvalidOperationException("Sample must permit both a decrease and an increase");
        improved = (byte)(initial - 5);
        var runner = new FreezeRunner(session, path);
        runner.SetPlayer(player, [(attribute, FreezeMode.AllowDecrease)]);
        session.Writer.SetAttributeRaw(player, attribute, improved);
        if (runner.Tick() != 0) throw new StaleObjectException("Decrease was not accepted");
    }
    using var reconnected = Attach(writable: true);
    var fresh = reconnected.Players.LoadAll().Single(p => p.Id == id);
    var restored = new FreezeRunner(reconnected, path);
    var field = restored.Players.Single(p => p.Id == id).Fields.Single(f => f.Attribute == attribute);
    if (field.Mode != FreezeMode.AllowDecrease || field.Value != improved) throw new StaleObjectException("Freeze mode or value was not restored");
    reconnected.Writer.SetAttributeRaw(fresh, attribute, (byte)(improved + 5));
    if (restored.Tick() != 1 || reconnected.Players.Reload(fresh.Address)?.Raw(attribute) != improved)
        throw new StaleObjectException("Reconnected freezer did not correct the value");
    Console.WriteLine($"{id} {attribute}: reconnected AllowDecrease at {improved}, corrected increase");
    return 0;
}

int FreezeDecreaseCheck(int id, PlayerAttribute attribute)
{
    using var session = Attach(writable: true);
    var player = session.Players.LoadAll().Single(p => p.Id == id);
    byte initial = player.Raw(attribute);
    if (initial is < 6 or > 95) throw new InvalidOperationException("Sample must permit both a decrease and an increase");
    var freezer = FrozenPlayer.Capture(player, [(attribute, FreezeMode.AllowDecrease)]);
    session.Writer.SetAttributeRaw(player, attribute, (byte)(initial - 5));
    if (freezer.Reconcile(session.Players, session.Writer) != 0) throw new StaleObjectException("Decrease was not accepted");
    var improved = session.Players.Reload(player.Address) ?? throw new StaleObjectException("Player disappeared");
    session.Writer.SetAttributeRaw(improved, attribute, initial);
    if (freezer.Reconcile(session.Players, session.Writer) != 1) throw new StaleObjectException("Increase was not corrected");
    byte after = session.Players.Reload(player.Address)!.Raw(attribute);
    if (after != initial - 5) throw new StaleObjectException("Freezer value did not persist");
    Console.WriteLine($"{id} {attribute}: {initial} -> {initial - 5} -> {initial} -> {after}");
    return 0;
}

int FreezeCheck(int id, PlayerAttribute attribute, int temporary)
{
    using var session = Attach(writable: true);
    var player = session.Players.LoadAll().Single(p => p.Id == id);
    var freeze = FrozenPlayer.Capture(player, [(attribute, FreezeMode.Fixed)]);
    int before = player.Attribute(attribute);
    session.Writer.SetAttribute(player, attribute, temporary);
    int changed = session.Players.Reload(player.Address)!.Attribute(attribute);
    int corrections = freeze.Reconcile(session.Players, session.Writer);
    int after = session.Players.Reload(player.Address)!.Attribute(attribute);
    Console.WriteLine($"{id} {player.DisplayName} {attribute}: {before} -> {changed} -> {after}, corrections {corrections}");
    return 0;
}

// freezebalancecheck <club id> <temporary balance>
int FreezeBalanceCheck(int id, long temporary)
{
    using var session = Attach(writable: true);
    var club = session.LoadClubs(session.Players.LoadAll()).Single(c => c.Id == id);
    var access = session.ClubFields;
    var frozen = Fm27Editor.Core.Clubs.ClubBalanceFreezer.Capture(club, access, FreezeMode.AllowIncrease);
    long before = frozen.Value;
    access.Set(club.Address, Fm27Editor.Core.Clubs.ClubField.Balance, before, temporary);
    bool corrected = frozen.Reconcile(session.Memory, session.Profile, access);
    long? after = access.Read(club.Address, Fm27Editor.Core.Clubs.ClubField.Balance);
    Console.WriteLine($"{id} {club.DisplayName} balance: {before} -> {temporary} -> {after}, corrected {corrected}");
    return 0;
}

// freezestorecheck <temporary file> <player id> <club id>
int FreezeStoreCheck(string path, int playerId, int clubId)
{
    using var session = Attach(writable: false);
    var players = session.Players.LoadAll();
    var clubs = session.LoadClubs(players);
    var player = players.Single(p => p.Id == playerId);
    var club = clubs.Single(c => c.Id == clubId);
    var frozenPlayer = FrozenPlayer.Capture(player, [(PlayerAttribute.Dribbling, FreezeMode.AllowIncrease)]);
    var frozenClub = Fm27Editor.Core.Clubs.ClubBalanceFreezer.Capture(club, session.ClubFields, FreezeMode.AllowIncrease);
    string key = FreezeStore.SaveKey(session);
    FreezeStore.Save(path, key, FreezeStore.Snapshot([frozenPlayer], [frozenClub]));
    var resolved = FreezeStore.Resolve(FreezeStore.Load(path, key), players, clubs);
    if (resolved.Players.Count != 1 || resolved.Clubs.Count != 1) throw new InvalidOperationException("Freeze entries did not resolve");
    Console.WriteLine($"key {key}; player {resolved.Players[0].Id} raw {resolved.Players[0].Fields.Single().Value}; club {resolved.Clubs[0].Id} balance {resolved.Clubs[0].Value}");
    return 0;
}

// freezeruncheck <temporary file> <player id> <temporary displayed dribbling>
int FreezeRunCheck(string path, int id, int temporary)
{
    int before;
    using (var firstSession = Attach(writable: true))
    {
        var firstPlayer = firstSession.Players.LoadAll().Single(p => p.Id == id);
        before = firstPlayer.Attribute(PlayerAttribute.Dribbling);
        new FreezeRunner(firstSession, path).SetPlayer(firstPlayer, [(PlayerAttribute.Dribbling, FreezeMode.Fixed)]);
    }
    using var session = Attach(writable: true);
    var player = session.Players.LoadAll().Single(p => p.Id == id);
    var reconnected = new FreezeRunner(session, path);
    session.Writer.SetAttribute(player, PlayerAttribute.Dribbling, temporary);
    int corrected = reconnected.Tick();
    int after = session.Players.Reload(player.Address)!.Attribute(PlayerAttribute.Dribbling);
    Console.WriteLine($"{id}: stored {before}, changed {temporary}, restored {after}, corrections {corrected}");
    return 0;
}

// exportplayers <csv|json> <path> [player id ...]
int ExportPlayers(string format, string path, int[] ids)
{
    if (format is not ("csv" or "json")) throw new ArgumentOutOfRangeException(nameof(format), "Expected csv or json");
    using var session = Attach(writable: false);
    var wanted = ids.ToHashSet();
    var players = session.Players.LoadAll().Where(p => ids.Length == 0 || p.Id is { } id && wanted.Contains(id)).ToArray();
    var date = session.ReadGameDate();
    foreach (var player in players) player.GameDate = date;
    using var output = File.Create(path);
    if (format == "csv") PlayerExport.WriteCsv(output, players); else PlayerExport.WriteJson(output, players);
    Console.WriteLine($"{players.Length} players exported to {path}");
    return 0;
}

int KeepPlayingBatch(string[] args)
{
    bool write = args.Contains("--write");
    var ids = args.Where(a => a != "--write").Select(int.Parse).ToArray();
    if (ids.Length == 0 || ids.Distinct().Count() != ids.Length) throw new ArgumentException("Provide unique player IDs");
    using var session = Attach(writable: write);
    var all = session.Players.LoadAll();
    var writer = new KeepPlayingWriter(session.Memory, session.Profile, session.ModuleBase);
    var plans = ids.Select(id => writer.Plan(all.Single(p => p.Id == id))).ToArray();
    Console.WriteLine($"{plans.Length} players planned");
    if (!write) { Console.WriteLine("plan only; add --write to apply"); return 0; }
    foreach (var preview in plans)
    {
        var current = writer.Replan(preview);
        writer.Apply(current);
        Console.WriteLine($"{current.Player.Id}: applied");
    }
    var reader = new PlannedRetirements(session.Memory, session.Profile, session.ModuleBase);
    var remaining = reader.ReadAll();
    foreach (var preview in plans)
    {
        byte traits = session.Memory.Read(preview.Player.Address + (ulong)session.Profile.PreferredMoves!.Value + 6, 1)[0];
        if (remaining.ContainsKey(preview.PersonId) || (traits & 3) != 2) throw new StaleObjectException("Retirement verification failed");
        Console.WriteLine($"{preview.Player.Id}: no retirement, trait {traits:X2}");
    }
    return 0;
}

int CaptainCommand(string[] args)
{
    bool write = args.Contains("--write");
    int restoreIndex = Array.IndexOf(args, "--restore");
    ulong? restore = restoreIndex >= 0
        ? Convert.ToUInt64(args[restoreIndex + 1].Replace("0x", ""), 16)
        : null;
    var words = args.Where((_, i) => i != restoreIndex && (restoreIndex < 0 || i != restoreIndex + 1) && args[i] != "--write").ToArray();
    int id = int.Parse(words[0]);
    var role = words.Length > 1 && words[1].Equals("vice", StringComparison.OrdinalIgnoreCase) ? CaptainRole.ViceCaptain : CaptainRole.Captain;
    using var session = Attach(writable: write);
    var player = session.Players.LoadAll().Single(p => p.Id == id);
    var service = new TeamCaptainService(session.Memory, session.Profile, session.ModuleBase);
    var plan = service.Plan(player, role, restore);
    Console.WriteLine($"{id} {player.DisplayName}: {TeamCaptainService.Label(role)} team 0x{plan.Team:X} club 0x{plan.Club:X} person 0x{plan.Person:X} (was 0x{plan.Previous:X})");
    if (!write) { Console.WriteLine("plan only; add --write to apply"); return 0; }
    service.Apply(plan);
    ulong after = BitConverter.ToUInt64(session.Memory.Read(plan.Address, 8), 0);
    if (after != plan.Person) throw new StaleObjectException("Captain readback failed");
    Console.WriteLine($"applied; {TeamCaptainService.Label(role)} is 0x{after:X}");
    return 0;
}

int KeepPlaying(int id, bool write)
{
    using var session = Attach(writable: write);
    var player = session.Players.LoadAll().Single(p => p.Id == id);
    var writer = new KeepPlayingWriter(session.Memory, session.Profile, session.ModuleBase);
    var plan = writer.Plan(player);
    Console.WriteLine($"{id} {player.DisplayName}: retirement {plan.RetirementDate?.ToString("yyyy-MM-dd") ?? "none"}, {plan.Writes.Count} writes");
    if (!write) { Console.WriteLine("plan only; add --write to apply"); return 0; }
    writer.Apply(plan);
    var reader = new PlannedRetirements(session.Memory, session.Profile, session.ModuleBase);
    Console.WriteLine($"applied; retirement {(reader.ReadAll().ContainsKey(plan.PersonId) ? "present" : "none")}; trait byte {session.Memory.Read(player.Address + (ulong)session.Profile.PreferredMoves!.Value + 6, 1)[0]:X2}");
    return 0;
}

int MaxAttributesCommand(int id, bool write)
{
    using var session = Attach(writable: write);
    var player = session.Players.LoadAll().Single(p => p.Id == id);
    var plan = MaxAttributes.Plan([player]);
    Console.WriteLine($"{id} {player.DisplayName}: {plan.Changes.Count} changes, {plan.Unavailable} unavailable");
    if (!write) { Console.WriteLine("plan only; add --write to apply"); return 0; }
    foreach (var change in plan.Changes) PlayerMassEdit.Apply(change, session.Players, session.Writer);
    var after = session.Players.Reload(player.Address) ?? throw new StaleObjectException("Player no longer readable");
    int correct = plan.Changes.Count(c => after.Raw(c.Attribute) == c.AfterRaw);
    Console.WriteLine($"verified {correct}/{plan.Changes.Count}; Crossing {after.Attribute(PlayerAttribute.Crossing)}, Dirtiness {after.Attribute(PlayerAttribute.Dirtiness)}, InjuryProneness {after.Attribute(PlayerAttribute.InjuryProneness)}");
    if (correct != plan.Changes.Count) throw new StaleObjectException("Some values changed after application");
    return 0;
}

int StaffMove(int id, int buyerId, bool write, byte? newJob = null, int? newWage = null)
{
    using var session = Attach(writable: write);
    var staff = session.Staff.LoadAll().Single(s => s.Id == id);
    var clubs = session.LoadClubs(session.Players.LoadAll());
    var from = clubs.Single(c => c.Address == staff.Club?.Address);
    var to = clubs.Single(c => c.Id == buyerId);
    var service = new Fm27Editor.Core.Staff.StaffMoveService(session.Memory, session.Profile, session.ModuleBase);
    var plan = service.Plan(staff, from, to, session.ReadGameDate(), newJob, newWage);
    Console.WriteLine($"{staff.DisplayName}: {from.DisplayName} -> {to.DisplayName}, {string.Join(", ", plan.Departments)}, {plan.Writes.Count} writes");
    if (!write) { Console.WriteLine("plan only; add --write to apply"); return 0; }
    service.Apply(plan);
    var after = session.Staff.Reload(staff.Address) ?? throw new StaleObjectException("Staff no longer readable");
    Console.WriteLine($"applied; club now {after.Club?.DisplayName ?? "-"}, job {after.JobCode}, wage {session.StaffContracts.Read(after)?.Wage}");
    return 0;
}

int StaffMoveSurvey(int id)
{
    using var session = Attach(writable: false);
    var staff = session.Staff.LoadAll().Single(s => s.Id == id);
    var clubs = session.LoadClubs(session.Players.LoadAll());
    var from = clubs.Single(c => c.Address == staff.Club?.Address);
    var service = new Fm27Editor.Core.Staff.StaffMoveService(session.Memory, session.Profile, session.ModuleBase);
    int found = 0;
    var failures = new Dictionary<string, int>();
    foreach (var club in clubs.Where(c => c.Address != from.Address && c.Id is > 0))
    {
        try
        {
            var plan = service.Plan(staff, from, club);
            Console.WriteLine($"{club.Id} {club.DisplayName}: {string.Join(", ", plan.Departments)}; {plan.Writes.Count} writes");
            if (++found == 5) break;
        }
        catch (Exception e) when (e is InvalidOperationException or MemoryAccessException or StaleObjectException)
        { failures[e.Message] = failures.GetValueOrDefault(e.Message) + 1; }
    }
    Console.WriteLine($"{found} candidates; read only");
    if (found == 0) foreach (var failure in failures.OrderByDescending(x => x.Value).Take(5)) Console.WriteLine($"  {failure.Value}: {failure.Key}");
    return 0;
}

int StaffById(int[] ids)
{
    using var session = Attach(writable: false);
    var wanted = ids.ToHashSet();
    var staff = session.Staff.LoadAll();
    Console.WriteLine($"{staff.Count} staff read");
    foreach (var person in staff.Where(p => wanted.Contains(p.Id)))
        Console.WriteLine($"{person.Id} {person.DisplayName}: CA {person.CurrentAbility}, PA {person.PotentialAbility}, club {person.Club?.DisplayName ?? "-"}, team {person.TeamName ?? "-"}, person 0x{person.Address:X}, staff 0x{person.StaffAddress:X}");
    return 0;
}

int SetStaffAbilityById(int id, int ca, int pa)
{
    using var session = Attach(writable: true);
    var staff = session.Staff.LoadAll().Single(s => s.Id == id);
    session.StaffWriter.SetAbility(staff, ca, pa);
    var after = session.Staff.Reload(staff.Address) ?? throw new StaleObjectException("Staff no longer readable");
    Console.WriteLine($"{id} {staff.DisplayName}: CA {staff.CurrentAbility} -> {after.CurrentAbility}, PA {staff.PotentialAbility} -> {after.PotentialAbility}");
    return 0;
}

int StaffContractById(int id, int? wage)
{
    using var session = Attach(writable: wage is not null);
    var staff = session.Staff.LoadAll().Single(s => s.Id == id);
    var contract = session.StaffContracts.Read(staff) ?? throw new StaleObjectException("Staff contract is not readable");
    Console.WriteLine($"{id} {staff.DisplayName}: wage {contract.Wage}, type {contract.Type}, rolling {contract.Rolling}, started {contract.Started:yyyy-MM-dd}, expires {contract.Expires:yyyy-MM-dd}");
    if (wage is not { } newWage) return 0;
    if (contract.Started is not { } started || contract.Expires is not { } expires)
        throw new InvalidOperationException("Staff contract dates are not readable");
    session.StaffContracts.Set(staff, contract, new OfferContractFields(newWage, (ContractType)contract.Type, contract.Rolling, started, expires));
    var after = session.StaffContracts.Read(staff) ?? throw new StaleObjectException("Staff contract is no longer readable");
    Console.WriteLine($"{id} {staff.DisplayName}: wage {contract.Wage} -> {after.Wage}");
    return 0;
}


// staffrenew <staffId> [years]: extends the staff contract (write)
int StaffRenewCommand(int id, int years)
{
    using var session = Attach(writable: true);
    var staff = session.Staff.LoadAll().Single(s => s.Id == id);
    var contract = session.StaffContracts.Read(staff) ?? throw new StaleObjectException("Staff contract is not readable");
    var today = session.ReadGameDate() ?? throw new InvalidOperationException("The in-game date could not be read");
    Console.WriteLine($"{staff.DisplayName}: expires {contract.Expires:yyyy-MM-dd} -> renew {years} years from {today:yyyy-MM-dd}");
    session.StaffContracts.Renew(staff, contract, years, today);
    var after = session.StaffContracts.Read(staff) ?? throw new StaleObjectException("Staff contract is no longer readable");
    Console.WriteLine($"  now expires {after.Expires:yyyy-MM-dd}");
    return 0;
}

// staffexpire <staffId>: ends the staff contract yesterday (write)
int StaffExpireCommand(int id)
{
    using var session = Attach(writable: true);
    var staff = session.Staff.LoadAll().Single(s => s.Id == id);
    var contract = session.StaffContracts.Read(staff) ?? throw new StaleObjectException("Staff contract is not readable");
    var today = session.ReadGameDate() ?? throw new InvalidOperationException("The in-game date could not be read");
    Console.WriteLine($"{staff.DisplayName}: expires {contract.Expires:yyyy-MM-dd} -> expire");
    session.StaffContracts.Expire(staff, contract, today);
    var after = session.StaffContracts.Read(staff) ?? throw new StaleObjectException("Staff contract is no longer readable");
    Console.WriteLine($"  now expires {after.Expires:yyyy-MM-dd}");
    return 0;
}

int Status()
{
    using var session = Attach(writable: true);
    Console.WriteLine($"build {session.Profile.Build}, access {(session.CanWrite ? "read/write" : "read-only")}");
    return 0;
}

int Players(string? filter)
{
    using var session = Attach(writable: false);
    var sw = Stopwatch.StartNew();
    var players = session.Players.LoadAll();
    var today = session.ReadGameDate();
    Console.WriteLine($"build {session.Profile.Build}{(session.Profile.ReadOnly ? " (limited writes)" : "")}: {players.Count} players in {sw.ElapsedMilliseconds} ms, game date {today?.ToString("yyyy-MM-dd") ?? "unknown"}");
    var shown = filter is null ? players.Take(20) : players.Where(p => Matches(p, filter));
    foreach (var p in shown)
        Console.WriteLine($"{p.DisplayName,-30} age {(today is { } t ? p.AgeOn(t) : null),-3} {p.Club?.DisplayName,-22} [{p.NationalityName,-12} | {p.DivisionName,-18}] £{p.Contract?.Wage,-7} to {p.Contract?.Expires} joined {p.Contract?.Joined}  " +
                          $"CA {p.CurrentAbility,3} PA {p.PotentialAbility,3}  {p.Height} cm  {p.DateOfBirth}  " +
                          $"Cro {p.Attribute(PlayerAttribute.Crossing)} Dri {p.Attribute(PlayerAttribute.Dribbling)} Pac {p.Attribute(PlayerAttribute.Pace)}  0x{p.Address:X}");
    return 0;
}

static bool Matches(PlayerRecord p, string filter) =>
    new[] { p.DisplayName, p.FullName }.Any(n => n?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true);

int SetAttributeById(int id, PlayerAttribute attribute, int value)
{
    using var session = Attach(writable: true);
    var matches = session.Players.LoadAll().Where(p => p.Id == id).ToList();
    if (matches.Count != 1) throw new ArgumentOutOfRangeException(nameof(id), id, $"{matches.Count} players with ID {id}");
    var player = matches[0];
    int before = player.Attribute(attribute);
    session.Writer.SetAttribute(player, attribute, value);
    var after = session.Players.Reload(player.Address) ?? throw new StaleObjectException("Player no longer exists");
    Console.WriteLine($"{id} {player.DisplayName}: {attribute} {before} -> {after.Attribute(attribute)}");
    return 0;
}

int ContractById(int id)
{
    using var session = Attach(writable: false);
    var player = session.Players.LoadAll().Single(p => p.Id == id);
    var contract = player.Contract ?? throw new StaleObjectException("Player has no readable contract");
    Console.WriteLine($"{id} {player.DisplayName}: wage {contract.Wage}, expires {contract.Expires:yyyy-MM-dd}");
    return 0;
}

int CurrentLoanCommand(int id)
{
    using var session = Attach(writable: false);
    var player = session.Players.LoadAll().Single(p => p.Id == id);
    var result = new CurrentLoanReader(session.Memory, session.Profile, session.ModuleBase).Read(player);
    Console.WriteLine($"{id} {player.DisplayName}: {result.Status}; owner {player.ClubName}");
    if (result.Contract is { } c)
        Console.WriteLine($"  borrowing team 0x{result.BorrowingTeamAddress:X}; wages {c.Wage}; signed {result.Signed:yyyy-MM-dd}; {c.Started:yyyy-MM-dd} - {c.Expires:yyyy-MM-dd}");
    if (result.Amounts is { } a)
        Console.WriteLine($"  agreed {a.AgreedPrice}; buyback {a.BuyBackFee}; monthly {a.MonthlyFee}; unused monthly {a.UnusedMonthlyFee}");
    return result.Status == CurrentLoanStatus.Unavailable ? 2 : 0;
}

// loans: every player the game currently has out on loan, newest read (read-only).
int LoansCommand(string? filter)
{
    using var session = Attach(writable: false);
    var all = session.Players.LoadAll();
    var deals = session.TransferDeals;
    int found = 0;
    foreach (var p in all)
    {
        if (filter is { Length: > 0 } && !p.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
        if (session.ReadCurrentLoan(p) is not { Status: CurrentLoanStatus.Present } loan) continue;
        found++;
        Console.WriteLine($"{p.Id} {p.DisplayName}: {p.ClubName} -> {deals.TeamName(loan.BorrowingTeamAddress)}; signed {loan.Signed:yyyy-MM-dd}; " +
            $"contract {loan.Contract?.Started:yyyy-MM-dd} - {loan.Contract?.Expires:yyyy-MM-dd}; wage {loan.Contract?.Wage}; agreed {loan.Amounts?.AgreedPrice}");
    }
    Console.WriteLine($"{found} loan(s) of {all.Count:N0} players");
    return 0;
}

// loan show <id> | loan set <id> <field>=<value>... [--write] | loan recall <id> [--write]
// Fields: wage, started, expires, signed, agreed, buyback, monthly, unused. Dates are yyyy-MM-dd; money is -1 for none.
int LoanCommand(string[] a)
{
    if (a.Length < 2) { Console.WriteLine("usage: loan show <id> | loan set <id> <field>=<value>... [--write] | loan recall <id> [--write] | loan create <id> <club id> [team=0xADDR] [wagepct=100] [wage=N] [start=] [expires=] [agreed=] [buyback=] [monthly=] [after=] [recall=1] [--write]"); return 1; }
    if (a[0] == "create") return LoanCreateCommand(a);
    int id = int.Parse(a[1]);
    bool write = a.Contains("--write");
    using var session = Attach(writable: write);
    var player = session.Players.LoadAll().Single(p => p.Id == id);
    var loan = session.ReadLoan(player);
    if (loan is null) { Console.WriteLine($"{id} {player.DisplayName}: no active loan"); return 1; }

    string Team(ulong team) => session.TransferDeals.TeamName(team) ?? $"0x{team:X}";
    string Terms(LoanTerms t) =>
        $"wage {t.Wage}; {t.Started:yyyy-MM-dd} - {t.Expires:yyyy-MM-dd}; signed {t.Signed:yyyy-MM-dd}; " +
        $"agreed {t.AgreedPrice}; buyback {t.BuyBackFee}; monthly {t.MonthlyFee}; unused {t.UnusedMonthlyFee}";

    switch (a[0])
    {
        case "show":
            Console.WriteLine($"{id} {player.DisplayName}: {Team(loan.BorrowingTeam)} on loan from {Team(loan.ParentTeam)}; " +
                $"contract 0x{loan.ContractAddress:X}; holder 0x{loan.Holder:X}; loan contract 0x{loan.LoanContractAddress:X}");
            Console.WriteLine($"  {Terms(loan.Terms)}");
            return 0;

        case "set":
        {
            int? wage = null, agreed = null, buyback = null, monthly = null, unused = null;
            DateOnly? started = null, expires = null, signed = null;
            foreach (var pair in a[2..])
            {
                if (pair == "--write") continue;
                int eq = pair.IndexOf('=');
                if (eq < 0) { Console.WriteLine($"not a field=value pair: {pair}"); return 1; }
                string key = pair[..eq].ToLowerInvariant(), value = pair[(eq + 1)..];
                if (key is "started" or "expires" or "signed")
                {
                    if (!DateOnly.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var date))
                    { Console.WriteLine($"not a date: {value}"); return 1; }
                    if (key == "started") started = date; else if (key == "expires") expires = date; else signed = date;
                }
                else
                {
                    if (!int.TryParse(value, out int number)) { Console.WriteLine($"not a number: {value}"); return 1; }
                    switch (key)
                    {
                        case "wage": wage = number; break;
                        case "agreed": agreed = number; break;
                        case "buyback": buyback = number; break;
                        case "monthly": monthly = number; break;
                        case "unused": unused = number; break;
                        default: Console.WriteLine($"unknown field: {key}"); return 1;
                    }
                }
            }
            var edit = new LoanTermsEdit(wage, started, expires, signed, agreed, buyback, monthly, unused);
            Console.WriteLine($"before: {Terms(loan.Terms)}");
            if (!write)
            {
                Console.WriteLine($"plan:     wage {wage?.ToString() ?? "-"}; {started:yyyy-MM-dd} - {expires:yyyy-MM-dd}; signed {signed:yyyy-MM-dd}; " +
                    $"agreed {agreed?.ToString() ?? "-"}; buyback {buyback?.ToString() ?? "-"}; monthly {monthly?.ToString() ?? "-"}; unused {unused?.ToString() ?? "-"}");
                Console.WriteLine("(read-only; add --write to change the save)");
                return 0;
            }
            session.SetLoanTerms(loan, edit);
            var after = session.ReadLoan(player) ?? throw new InvalidOperationException("The loan disappeared after the write");
            Console.WriteLine($"after:  {Terms(after.Terms)}");
            return 0;
        }

        case "recall":
        {
            var plan = session.PlanLoanRecall(player);
            Console.WriteLine($"{id} {player.DisplayName}: recall from {Team(plan.BorrowingTeam)} to {Team(plan.ParentTeam)}; " +
                $"leaving squad index {plan.SquadIndex}; {plan.Writes.Count} writes{(plan.NeedsAllocation ? "; parent squad needs a new buffer" : "")}");
            if (!write) { Console.WriteLine("(read-only; add --write to change the save)"); return 0; }
            session.ApplyLoanRecall(plan);
            var after = session.ReadLoan(player);
            var squad = session.ReadSquad(plan.ParentTeam);
            Console.WriteLine($"after:  playing team {Team(BitConverter.ToUInt64(session.Memory.Read(player.Address + (ulong)session.Profile.Team!.PlayerTeam, 8), 0))}; " +
                $"loan {(after is null ? "cleared" : "still present")}; parent squad {squad.Count} contains player: {squad.Any(p => p.Id == id)}");
            return 0;
        }

        default:
            Console.WriteLine($"unknown loan action: {a[0]}");
            return 1;
    }
}

// loan create <player id> <club id> [team=0xADDR] [wagepct=100] [wage=N] [start=yyyy-MM-dd] [expires=yyyy-MM-dd] [agreed=N] [buyback=N]
//             [monthly=N] [after=N] [recall=1] [--write]
// Loans the player out like FMRTE's Move → Loan. Without --write it only prints the plan and every byte it would change.
// Defaults are FMRTE's: wage 100% of the contract wage, start = game date, expires = start + 1 year, no prices, no fee.
int LoanCreateCommand(string[] a)
{
    if (a.Length < 3 || !int.TryParse(a[1], out int id) || !int.TryParse(a[2], out int clubId))
    { Console.WriteLine("usage: loan create <player id> <club id> [team=0xADDR] [wagepct=100] [wage=N] [start=] [expires=] [agreed=N] [buyback=N] [monthly=N] [after=N] [recall=1] [--write]"); return 1; }
    bool write = a.Contains("--write");
    using var session = Attach(writable: write);
    var all = session.Players.LoadAll();
    var player = all.Single(p => p.Id == id);
    var clubs = session.LoadClubs(all);
    var club = clubs.SingleOrDefault(c => c.Id == clubId) ?? throw new InvalidOperationException($"no club with id {clubId}");
    ulong? teamAddress = null;
    int? wage = null, agreed = null, buyback = null, after = null; int wagePct = 100, monthly = 0; bool recall = false;
    DateOnly? start = null, expires = null;
    foreach (var pair in a[3..])
    {
        if (pair == "--write") continue;
        int eq = pair.IndexOf('=');
        if (eq < 0) { Console.WriteLine($"not a field=value pair: {pair}"); return 1; }
        string key = pair[..eq].ToLowerInvariant(), value = pair[(eq + 1)..];
        switch (key)
        {
            case "team": teamAddress = Convert.ToUInt64(value.Replace("0x", ""), 16); break;
            case "wagepct": wagePct = int.Parse(value); break;
            case "wage": wage = int.Parse(value); break;
            case "agreed": agreed = int.Parse(value); break;
            case "buyback": buyback = int.Parse(value); break;
            case "monthly": monthly = int.Parse(value); break;
            case "after": after = int.Parse(value); break;
            case "recall": recall = value != "0"; break;
            case "start": start = DateOnly.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
            case "expires": expires = DateOnly.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
            default: Console.WriteLine($"unknown field: {key}"); return 1;
        }
    }
    var team = teamAddress ?? club.FirstTeam?.Address ?? throw new InvalidOperationException("the club has no first team");
    var today = session.ReadGameDate() ?? throw new InvalidOperationException("the game date could not be read");
    start ??= today;
    expires ??= start.Value.AddYears(1);
    int contractWage = session.Contracts.Read(player)?.Wage ?? throw new InvalidOperationException("the player's contract could not be read");
    var offer = new LoanOffer(wage ?? LoanCreator.WageFromPercent(contractWage, wagePct), start.Value, expires.Value, agreed, buyback, monthly, after ?? 0, recall);
    var plan = session.PlanLoanCreate(player, team, offer, all);
    Console.WriteLine($"{player.DisplayName}: {player.ClubName} -> {club.DisplayName} (team 0x{team:X}); wage {offer.Wage} of {contractWage}; {offer.Start:yyyy-MM-dd} - {offer.Expires:yyyy-MM-dd}; " +
        $"agreed {agreed?.ToString() ?? "none"}; buyback {buyback?.ToString() ?? "none"}; monthly {monthly}; purchase after {after?.ToString() ?? "none"}");
    Console.WriteLine($"template: loan contract 0x{plan.TemplateContract:X} ({(plan.TemplateFromBorrowingTeam ? "same borrowing team" : "first loan in the game, none at this team")}); " +
        $"{plan.Writes.Count} pointer writes{(plan.NeedsAllocation ? "; squad full, a new squad buffer will be allocated" : "")}");
    foreach (var w in plan.Writes.Where(w => w.Value.Length > 0)) Console.WriteLine($"  {w.What}: {Convert.ToHexString(w.Expected)} -> {Convert.ToHexString(w.Value)}");
    for (int i = 0; i < plan.Contract.Length; i += 16) Console.WriteLine($"  contract +0x{i:X2}  {Convert.ToHexString(plan.Contract.AsSpan(i, 16))}");
    if (!write) { Console.WriteLine("plan only; add --write to apply"); return 0; }
    session.ApplyLoanCreate(plan);
    var loan = session.ReadLoan(session.Players.Reload(player.Address)!);
    Console.WriteLine(loan is null ? "applied, but the loan does not read back" :
        $"applied: wage {loan.Terms.Wage}; {loan.Terms.Started:yyyy-MM-dd} - {loan.Terms.Expires:yyyy-MM-dd}; agreed {loan.Terms.AgreedPrice}; buyback {loan.Terms.BuyBackFee}; monthly {loan.Terms.MonthlyFee}");
    return loan is null ? 2 : 0;
}

// compare <name|id> <name|id>   side-by-side attribute, position and personality table with differences marked
int CompareCommand(string first, string second)
{
    using var session = Attach(writable: false);
    var all = session.Players.LoadAll();
    PlayerRecord Find(string text)
    {
        var matches = all.Where(p => int.TryParse(text, out int id) ? p.Id == id : p.DisplayName.Equals(text, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count != 1) throw new InvalidOperationException($"{matches.Count} players named '{text}'");
        return matches[0];
    }
    var a = Find(first);
    var b = Find(second);
    if (session.ReadGameDate() is { } today) { a.GameDate = today; b.GameDate = today; }
    string D(DateOnly? d) => d is { } x ? x.ToString("yyyy-MM-dd") : "-";
    Console.WriteLine($"{a.DisplayName,-24} {b.DisplayName,-24}");
    Console.WriteLine($"{"club",-16} {a.ClubName,-24} {b.ClubName,-24}");
    Console.WriteLine($"{"division",-16} {a.DivisionName,-24} {b.DivisionName,-24}");
    Console.WriteLine($"{"age / born",-16} {a.Age?.ToString() ?? "-",-4} {D(a.DateOfBirth),-19} {b.Age?.ToString() ?? "-",-4} {D(b.DateOfBirth),-19}");
    Console.WriteLine($"{"height",-16} {a.Height + " cm",-24} {b.Height + " cm",-24}");
    Console.WriteLine($"{"CA / PA",-16} {$"{a.CurrentAbility} / {a.PotentialAbility}",-24} {$"{b.CurrentAbility} / {b.PotentialAbility}",-24}");
    Console.WriteLine($"{"reputation",-16} {$"{a.HomeReputation} / {a.CurrentReputation} / {a.WorldReputation}",-24} {$"{b.HomeReputation} / {b.CurrentReputation} / {b.WorldReputation}",-24}");
    Console.WriteLine($"{"wage",-16} {(a.Contract?.Wage.ToString() ?? "-"),-24} {(b.Contract?.Wage.ToString() ?? "-"),-24}");
    Console.WriteLine($"{"contract to",-16} {D(a.Contract?.Expires),-24} {D(b.Contract?.Expires),-24}");
    Console.WriteLine($"{"positions",-16} {a.PositionSummary,-24} {b.PositionSummary,-24}");

    Console.WriteLine();
    Console.WriteLine($"{"attribute",-22} {a.DisplayName,-10} {b.DisplayName,-10} diff");
    foreach (PlayerAttribute attribute in Enum.GetValues<PlayerAttribute>())
    {
        int va = a.Attribute(attribute), vb = b.Attribute(attribute);
        if (va == vb && va < 0) continue;
        Console.WriteLine($"{attribute,-22} {va,10} {vb,10} {(va == vb ? "" : "*")}");
    }

    Console.WriteLine();
    Console.WriteLine($"{"position",-22} {a.DisplayName,-10} {b.DisplayName,-10} diff");
    foreach (PlayerPosition position in Enum.GetValues<PlayerPosition>())
    {
        int va = a.Position(position), vb = b.Position(position);
        Console.WriteLine($"{position,-22} {va,10} {vb,10} {(va == vb ? "" : "*")}");
    }

    Console.WriteLine();
    Console.WriteLine($"{"personality",-22} {a.DisplayName,-10} {b.DisplayName,-10} diff");
    foreach (PersonalityAttribute attribute in Enum.GetValues<PersonalityAttribute>())
    {
        int va = a.PersonalityValue(attribute), vb = b.PersonalityValue(attribute);
        Console.WriteLine($"{attribute,-22} {va,10} {vb,10} {(va == vb ? "" : "*")}");
    }
    return 0;
}
// refsurvey   value ranges of every referee field (read-only, for writer limits)
int RefereeSurveyCommand()
{
    using var session = Attach(writable: false);
    var all = session.ReadReferees();
    void Range(string label, Func<RefereeRecord, int> get)
    {
        var values = all.Select(get).ToList();
        var groups = values.GroupBy(v => v).OrderBy(g => g.Key).Select(g => $"{g.Key}x{g.Count()}").ToList();
        Console.WriteLine($"{label,-18} min {values.Min(),4} max {values.Max(),4}  {string.Join(" ", groups.Take(14))}{(groups.Count > 14 ? " ..." : "")}");
    }
    Range("CA", r => r.CurrentAbility);
    Range("PA", r => r.PotentialAbility);
    Range("Reputation", r => r.Reputation);
    Range("AllowingFlow", r => r.AllowingFlow);
    Range("Discipline", r => r.Discipline);
    Range("ImportantMatches", r => r.ImportantMatches);
    Range("Pressure", r => r.Pressure);
    Range("Refereeing", r => r.Refereeing);
    Range("RunningLine", r => r.RunningLine);
    Range("TimeKeeping", r => r.TimeKeeping);
    Range("EliteList", r => r.EliteList);
    Range("Continental", r => r.Continental);
    Range("EuropeanCategory", r => r.EuropeanCategory);
    Console.WriteLine($"null names: {all.Count(r => string.IsNullOrWhiteSpace(r.DisplayName))}; distinct ids {all.Select(r => r.UniqueId).Distinct().Count()}");
    return 0;
}
// referees [name]   list the game's referees (read-only)
int RefereesCommand(string? filter)
{
    using var session = Attach(writable: false);
    var sw = Stopwatch.StartNew();
    var all = session.ReadReferees();
    Console.WriteLine($"{all.Count:N0} referees in {sw.ElapsedMilliseconds} ms");
    int shown = 0;
    foreach (var r in all)
    {
        if (filter is { Length: > 0 } && !r.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
        Console.WriteLine($"{r.UniqueId,8} {r.DisplayName,-28} CA {r.CurrentAbility,3} PA {r.PotentialAbility,3} rep {r.Reputation,5} " +
            $"flow {r.AllowingFlow,2} disc {r.Discipline,2} big {r.ImportantMatches,2} press {r.Pressure,2} ref {r.Refereeing,2} " +
            $"line {r.RunningLine,2} time {r.TimeKeeping,2} elite {r.EliteList} cont {r.Continental} euro {r.EuropeanCategory} born {r.DateOfBirth:yyyy-MM-dd}");
        shown++;
    }
    Console.WriteLine($"{shown} shown");
    return 0;
}

// refset <id|name> <field> <value> [--write]
int RefereeSetCommand(string query, string field, int value, bool write)
{
    using var session = Attach(writable: write);
    var all = session.ReadReferees();
    var matches = all.Where(r => int.TryParse(query, out int id) ? r.UniqueId == id : r.DisplayName.Equals(query, StringComparison.OrdinalIgnoreCase)).ToList();
    if (matches.Count != 1) { Console.Error.WriteLine($"{matches.Count} referees match '{query}'"); return 2; }
    var referee = matches[0];
    var parsed = Enum.Parse<RefereeField>(field, true);
    int Before(RefereeRecord r) => parsed switch
    {
        RefereeField.CurrentAbility => r.CurrentAbility, RefereeField.PotentialAbility => r.PotentialAbility,
        RefereeField.Reputation => r.Reputation, RefereeField.AllowingFlow => r.AllowingFlow,
        RefereeField.Discipline => r.Discipline, RefereeField.ImportantMatches => r.ImportantMatches,
        RefereeField.Pressure => r.Pressure, RefereeField.Refereeing => r.Refereeing,
        RefereeField.RunningLine => r.RunningLine, RefereeField.TimeKeeping => r.TimeKeeping,
        RefereeField.EliteList => r.EliteList, RefereeField.Continental => r.Continental,
        RefereeField.EuropeanCategory => r.EuropeanCategory, _ => 0,
    };
    Console.WriteLine($"{referee.UniqueId} {referee.DisplayName}: {parsed} {Before(referee)} -> {value}");
    if (!write) { Console.WriteLine("(read-only; add --write to change the save)"); return 0; }
    session.SetReferee(referee, parsed, value);
    var after = session.Referees.Read(referee.Address) ?? throw new InvalidOperationException("Referee disappeared after the write");
    Console.WriteLine($"after: {parsed} {Before(after)}");
    return 0;
}
// globalscan   every begin/end pointer vector near the clubs anchor, with its first objects (read-only)
int GlobalScanCommand()
{
    using var session = Attach(writable: false);
    var memory = session.Memory;
    var module = session.Game;
    ulong size = module.ModuleEnd - module.ModuleBase;
    var slots = GlobalArrayFinder.FindClubsSlots(memory, module.ModuleBase, size)
        ?? throw new InvalidOperationException("clubs anchor signature not found");
    ulong S = slots.StartSlot;
    ulong Q(ulong at)
    {
        Span<byte> b = stackalloc byte[8];
        return memory.TryRead(at, b) ? BitConverter.ToUInt64(b) : 0;
    }
    bool InModule(ulong v) => v >= module.ModuleBase && v < module.ModuleBase + size;
    string? Name(ulong at)
    {
        ulong entry = Q(at);
        if (entry < 0x10000 || !InModule(Q(entry))) return null;
        ulong text = Q(entry);
        var raw = new byte[4];
        if (!memory.TryRead(text, raw)) return null;
        int n = BitConverter.ToInt32(raw);
        if (n <= 0 || n > 120) return null;
        var bytes = new byte[n];
        return memory.TryRead(text + 4, bytes) ? System.Text.Encoding.UTF8.GetString(bytes) : null;
    }
    for (long delta = -1024; delta <= 1024; delta += 8)
    {
        ulong slot = (ulong)((long)S + delta);
        ulong begin = Q(slot), end = Q(slot + 8);
        if (begin < 0x10000 || end <= begin || (end - begin) % 8 != 0) continue;
        long count = (long)(end - begin) / 8;
        if (count < 8 || count > 60000) continue;
        ulong first = Q(begin);
        if (first < 0x10000) continue;
        ulong vt = Q(first);
        if (!InModule(vt)) continue;
        var raw = memory.Read(first, 0xD0);
        ulong b2 = Q(begin + 8), b3 = Q(begin + 16);
        Console.WriteLine($"slot S{(delta >= 0 ? "+" : "")}{delta} (0x{slot:X}) count {count}: first 0x{first:X} vtable +0x{vt - module.ModuleBase:X}");
        Console.WriteLine($"    names '{Name(first + 40)}' '{Name(first + 48)}' '{Name(first + 56)}'; +0xB0 {BitConverter.ToString(raw, 0xA8, 0x18)}; " +
            $"ints +0xC8 {BitConverter.ToInt32(raw, 0xC8)} +0xCC {BitConverter.ToInt32(raw, 0xCC)}; next vt +0x{(Q(b2) >= module.ModuleBase ? Q(b2) - module.ModuleBase : 0):X}/+0x{(Q(b3) >= module.ModuleBase ? Q(b3) - module.ModuleBase : 0):X}");
    }
    return 0;
}
// globals [samples]   the clubs anchor and the arrays FM Studio derives from it, with object samples
int GlobalsCommand(int samples)
{
    using var session = Attach(writable: false);
    var memory = session.Memory;
    var module = session.Game;
    ulong size = module.ModuleEnd - module.ModuleBase;
    var arrays = GlobalArrayFinder.Read(memory, module.ModuleBase, size)
        ?? throw new InvalidOperationException("clubs anchor signature not found");
    Console.WriteLine($"clubs {arrays.ClubsCount:N0} (0x{arrays.ClubsBegin:X})  staff {arrays.StaffCount:N0} (0x{arrays.StaffBegin:X})  " +
        $"referees {arrays.RefereeCount:N0} (0x{arrays.RefereesBegin:X})  " +
        $"teams {arrays.TeamsCount:N0} (0x{arrays.TeamsBegin:X})");

    ulong Q(ulong at)
    {
        Span<byte> b = stackalloc byte[8];
        return memory.TryRead(at, b) ? BitConverter.ToUInt64(b) : 0;
    }
    string? Name(ulong at)
    {
        ulong entry = Q(at);
        if (entry < 0x10000) return null;
        ulong text = Q(entry);
        if (text < 0x10000) return null;
        var raw = new byte[4];
        if (!memory.TryRead(text, raw)) return null;
        int n = BitConverter.ToInt32(raw);
        if (n <= 0 || n > 200) return null;
        var bytes = new byte[n];
        return memory.TryRead(text + 4, bytes) ? System.Text.Encoding.UTF8.GetString(bytes) : null;
    }
    void Sample(string label, ulong begin, int count, int fieldCount, int[] int16Offsets)
    {
        Console.WriteLine($"=== {label}: {count:N0} objects at 0x{begin:X}");
        for (int i = 0; i < Math.Min(samples, count); i++)
        {
            ulong obj = Q(begin + (ulong)i * 8);
            if (obj < 0x10000) { Console.WriteLine($"  [{i}] empty"); continue; }
            Console.WriteLine($"  [{i}] 0x{obj:X} vtable 0x{Q(obj) - module.ModuleBase:X} " +
                $"first '{Name(obj + 40)}' last '{Name(obj + 48)}' common '{Name(obj + 56)}'");
            var raw = memory.Read(obj, fieldCount);
            Console.WriteLine($"      int16: {string.Join(", ", int16Offsets.Select(o => $"+0x{o:X}={BitConverter.ToInt16(raw, o)}"))}");
            Console.WriteLine($"      bytes +0xB0..+0xC0: {Convert.ToHexString(raw[0xB0..Math.Min(0xC0, raw.Length)])}");
            Console.WriteLine($"      ints +0xC8={BitConverter.ToInt32(raw, 0xC8)} +0xCC={BitConverter.ToInt32(raw, 0xCC)}");
            if (raw.Length >= 0x150) Console.WriteLine($"      hex +0x110..+0x150: {Convert.ToHexString(raw[0x110..0x150])}");
        }
    }
    Sample("referees", arrays.RefereesBegin, arrays.RefereeCount, 0x150, new[] { 284, 286, 288 });
    return 0;
}
// swap <name|id> <name|id> [--write]   exchange two players between their clubs for no fee
int SwapCommand(string first, string second, bool write)
{
    using var session = Attach(writable: write);
    var all = session.Players.LoadAll();
    PlayerRecord Find(string text)
    {
        var matches = all.Where(p => int.TryParse(text, out int id) ? p.Id == id : p.DisplayName.Equals(text, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count != 1) throw new InvalidOperationException($"{matches.Count} players named '{text}'");
        return matches[0];
    }
    var a = Find(first);
    var b = Find(second);
    var clubs = session.LoadClubs(all);
    var swap = new PlayerSwapService(session.Memory, session.Profile, session.ModuleBase);
    var firstTarget = swap.Target(b);
    var secondTarget = swap.Target(a);
    Console.WriteLine($"{a.DisplayName} ({a.ClubName}) <-> {b.DisplayName} ({b.ClubName})");
    foreach (var (player, target) in new[] { (a, firstTarget), (b, secondTarget) })
    {
        var leg = swap.Plan(player, target, clubs);
        Console.WriteLine($"  {player.DisplayName}: -> {leg.Buyer.DisplayName} ({leg.IntoTeam.SquadCount}/{leg.IntoTeam.SquadCapacity}) wage {leg.Plan.Wage}"
            + (leg.Plan.NeedsAllocation ? ", squad full: a new buffer will be allocated" : ""));
        foreach (var w in leg.Plan.Writes.Where(w => w.Value.Length == 4))
            Console.WriteLine($"      {w.What}: {BitConverter.ToInt32(w.Expected)} -> {BitConverter.ToInt32(w.Value)}");
    }
    if (!write) { Console.WriteLine("plan only; add --write to apply (the second leg is re-planned after the first)"); return 0; }
    var result = session.SwapPlayers(a, b);
    var afterA = session.Players.Reload(a.Address)!;
    var afterB = session.Players.Reload(b.Address)!;
    Console.WriteLine($"applied: {afterA.DisplayName} at {afterA.ClubName}; {afterB.DisplayName} at {afterB.ClubName}");
    return 0;
}
// nationtv <nation id|name> [set <men|women> <index> <value> --write]   the 20 transfer-value tiers
int NationTvCommand(string[] rest)
{
    bool write = rest.Contains("--write");
    using var session = Attach(writable: write);
    var nation = new Fm27Editor.Core.Nations.NationRepository(session.Memory, session.Profile, session.ModuleBase).LoadAll()
        .SingleOrDefault(n => int.TryParse(rest[0], out int id) ? n.Id == id : n.DisplayName.Equals(rest[0], StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"nation {rest[0]} not found");
    void Show(string label, bool women)
    {
        var values = session.ReadNationTransferValues(nation, women);
        Console.WriteLine(values is null
            ? $"{nation.Id} {nation.DisplayName} {label}: no transfer values"
            : $"{nation.Id} {nation.DisplayName} {label} 0x{values.ArrayAddress:X}: {string.Join(", ", values.Values)}");
    }
    Show("men", false);
    Show("women", true);
    int setAt = Array.IndexOf(rest, "set");
    if (setAt < 0) return 0;
    bool womens = rest[setAt + 1].Equals("women", StringComparison.OrdinalIgnoreCase);
    int index = int.Parse(rest[setAt + 2]), value = int.Parse(rest[setAt + 3]);
    var current = session.ReadNationTransferValues(nation, womens) ?? throw new InvalidOperationException("no transfer values to change");
    Console.WriteLine($"set {(womens ? "women" : "men")}[{index}] {current.Values[index]} -> {value}");
    if (!write) { Console.WriteLine("(read-only; add --write to change the save)"); return 0; }
    session.SetNationTransferValue(current, index, value);
    var after = session.ReadNationTransferValues(nation, womens) ?? throw new InvalidOperationException("transfer values disappeared after the write");
    Console.WriteLine($"after: {string.Join(", ", after.Values)}");
    return 0;
}
// allowat <hex address>   scan one object's pointers for a candidate allowance record (read-only)
int AllowAtCommand(ulong address)
{
    int[] offsets = [4586, 4587, 4588, 4589, 4590, 4591, 4595, 4596, 4597, 4598, 4599, 4600, 4605, 4606];
    using var session = Attach(writable: false);
    var memory = session.Memory;
    ulong Q(ulong at)
    {
        Span<byte> b = stackalloc byte[8];
        return memory.TryRead(at, b) ? BitConverter.ToUInt64(b) : 0;
    }
    bool Test(string label, ulong candidate)
    {
        if (candidate < 0x10000) return false;
        var bytes = new byte[14];
        for (int i = 0; i < offsets.Length; i++)
        {
            var one = new byte[1];
            if (!memory.TryRead(candidate + (ulong)offsets[i], one)) return false;
            bytes[i] = one[0];
        }
        int nonZero = bytes.Count(b => b != 0);
        if (nonZero < 6 || bytes.Any(b => b > 40)) return false;
        Console.WriteLine($"  HIT {label}: 0x{candidate:X} values {string.Join(",", bytes)}");
        return true;
    }
    Console.WriteLine($"object 0x{address:X}: vtable 0x{(Q(address) >= session.ModuleBase ? Q(address) - session.ModuleBase : Q(address)):X}");
    int hits = 0;
    for (int off = 0; off <= 0x2000; off += 8)
    {
        ulong v = Q(address + (ulong)off);
        if (v < 0x10000) continue;
        if (Test($"c+0x{off:X}", v)) hits++;
        if (off > 0x400) continue;
        for (int inner = 0; inner <= 0x400; inner += 8)
        {
            ulong w = Q(v + (ulong)inner);
            if (w >= 0x10000 && Test($"c+0x{off:X}->0x{inner:X}", w)) hits++;
        }
    }
    Console.WriteLine($"{hits} hits");
    return 0;
}
// allowhuman   try the human-staff mscc chain from FM Studio (staff+0x80 -> +0x370) on every manager (read-only)
int AllowHumanCommand()
{
    int[] offsets = [4586, 4587, 4588, 4589, 4590, 4591, 4595, 4596, 4597, 4598, 4599, 4600, 4605, 4606];
    using var session = Attach(writable: false);
    var memory = session.Memory;
    ulong Q(ulong at)
    {
        Span<byte> b = stackalloc byte[8];
        return memory.TryRead(at, b) ? BitConverter.ToUInt64(b) : 0;
    }
    var staff = session.Staff.LoadAll();
    var managers = staff.Where(s => s.Kind.Manager).ToList();
    Console.WriteLine($"{staff.Count:N0} staff, {managers.Count} managers");
    int hits = 0;
    foreach (var manager in managers)
    {
        foreach (var (label, baseAddress) in new[] { ("person", manager.Address), ("staff", manager.StaffAddress) })
        {
            if (baseAddress < 0x10000) continue;
            ulong c = Q(baseAddress + 0x80);
            if (c < 0x10000) continue;
            ulong mscc = Q(c + 0x370);
            Console.WriteLine($"  {manager.DisplayName} {label} 0x{baseAddress:X}: c 0x{c:X} mscc 0x{mscc:X}");
            if (mscc < 0x10000) continue;
            var bytes = new byte[14];
            bool ok = true;
            for (int i = 0; i < offsets.Length; i++)
            {
                var one = new byte[1];
                if (!memory.TryRead(mscc + (ulong)offsets[i], one)) { ok = false; break; }
                bytes[i] = one[0];
            }
            if (!ok) continue;
            int nonZero = bytes.Count(x => x != 0);
            if (nonZero < 8 || bytes.Any(x => x > 40)) continue;
            Console.WriteLine($"    HIT allowances {string.Join(",", bytes)}");
            hits++;
        }
    }
    Console.WriteLine($"{hits} hits");
    return 0;
}
// board <club id|name>   the club's board and executive staff (read-only)
int BoardCommand(string query)
{
    using var session = Attach(writable: false);
    var clubs = session.LoadClubs(session.Players.LoadAll());
    var club = int.TryParse(query, out int id)
        ? clubs.FirstOrDefault(c => c.Id == id)
        : clubs.FirstOrDefault(c => c.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase));
    if (club is null) { Console.Error.WriteLine($"no club for '{query}'"); return 2; }
    var board = ClubBoard.Members(session.Staff.LoadAll(), club.Address);
    Console.WriteLine($"{club.DisplayName}: {board.Count} board member(s)");
    foreach (var member in board)
        Console.WriteLine($"  {member.Role,-28} {member.Staff.DisplayName,-28} CA {member.Staff.CurrentAbility,3} PA {member.Staff.PotentialAbility,3} " +
            $"rep {member.Staff.CurrentReputation,5} job {member.Staff.JobCode} born {member.Staff.DateOfBirth:yyyy-MM-dd}");
    return 0;
}
// allowdeep <club id>   look for the staff-allowance record behind [team+0x80] (read-only)
int AllowDeepCommand(int clubId)
{
    int[] offsets = [4586, 4587, 4588, 4589, 4590, 4591, 4595, 4596, 4597, 4598, 4599, 4600, 4605, 4606];
    using var session = Attach(writable: false);
    var clubs = session.LoadClubs(session.Players.LoadAll());
    var club = clubs.FirstOrDefault(c => c.Id == clubId) ?? throw new InvalidOperationException($"club {clubId} not loaded");
    var memory = session.Memory;
    ulong Q(ulong at)
    {
        Span<byte> b = stackalloc byte[8];
        return memory.TryRead(at, b) ? BitConverter.ToUInt64(b) : 0;
    }
    bool Test(string label, ulong candidate)
    {
        if (candidate < 0x10000) return false;
        var bytes = new byte[14];
        for (int i = 0; i < offsets.Length; i++)
        {
            var one = new byte[1];
            if (!memory.TryRead(candidate + (ulong)offsets[i], one)) return false;
            bytes[i] = one[0];
        }
        int nonZero = bytes.Count(b => b != 0);
        if (nonZero < 8 || bytes.Any(b => b > 40)) return false;
        Console.WriteLine($"  HIT {label}: 0x{candidate:X} allowances {string.Join(",", bytes)}");
        return true;
    }
    foreach (var team in club.Teams.Take(3))
    {
        ulong c = Q(team.Address + 0x80);
        Console.WriteLine($"{club.DisplayName} team 0x{team.Address:X}: [team+0x80] = 0x{c:X}");
        if (c < 0x10000) continue;
        int hits = 0;
        for (int off = 0; off <= 0x4000; off += 8)
        {
            ulong v = Q(c + (ulong)off);
            if (v < 0x10000) continue;
            if (Test($"c+0x{off:X}", v)) hits++;
            if (off > 0x800) continue;
            for (int inner = 0; inner <= 0x800; inner += 8)
            {
                ulong w = Q(v + (ulong)inner);
                if (w >= 0x10000 && Test($"c+0x{off:X}->0x{inner:X}", w)) hits++;
            }
        }
        Console.WriteLine($"  {hits} candidates");
    }
    return 0;
}
// allowprobe: the FM Studio staff-allowance chain for one club (read-only measurement).
int AllowProbeCommand(int clubId)
{
    using var session = Attach(writable: false);
    var clubs = session.LoadClubs(session.Players.LoadAll());
    var club = clubs.FirstOrDefault(c => c.Id == clubId) ?? throw new InvalidOperationException($"club {clubId} not loaded");
    var memory = session.Memory;
    bool TryQ(ulong at, out ulong value)
    {
        Span<byte> b = stackalloc byte[8];
        value = 0;
        return at != 0 && memory.TryRead(at, b) && (value = BitConverter.ToUInt64(b)) != 0;
    }
    Console.WriteLine($"{club.Id} {club.DisplayName} club 0x{club.Address:X} teams {club.Teams.Count}");
    foreach (var team in club.Teams.Take(4))
    {
        Console.WriteLine($"  team 0x{team.Address:X} first {team.IsFirstTeam} squad {team.SquadCount}");
        if (!TryQ(team.Address + 0x80, out var holder)) { Console.WriteLine("    no [team+0x80]"); continue; }
        Console.WriteLine($"    [team+0x80] = 0x{holder:X} vtable 0x{(TryQ(holder, out var hv) ? hv - session.ModuleBase : 0):X}");
        if (!TryQ(holder + 0x370, out var mscc)) { Console.WriteLine("    no [holder+0x370]"); continue; }
        Console.WriteLine($"    mscc 0x{mscc:X} vtable 0x{(TryQ(mscc, out var mv) ? mv - session.ModuleBase : 0):X}");
        var raw = memory.Read(mscc + 0x11D0, 0x40);
        Console.WriteLine($"    bytes +0x11D0: {Convert.ToHexString(raw)}");
        var values = new byte[14];
        foreach (var (name, offset) in new (string, int)[] { ("Coach", 0x11EA), ("CoachU21", 0x11EB), ("CoachU18", 0x11EC),
            ("Physio", 0x11ED), ("PhysioU21", 0x11EE), ("PhysioU18", 0x11EF), ("SportSci", 0x11F3), ("SportSciU21", 0x11F4),
            ("SportSciU18", 0x11F5), ("PerfAnalyst", 0x11F6), ("PerfAnalystU21", 0x11F7), ("PerfAnalystU18", 0x11F8),
            ("Scouts", 0x11FD), ("Recruitment", 0x11FE) })
            Console.WriteLine($"    {name,-14} +0x{offset:X4} = {memory.Read(mscc + (ulong)offset, 1)[0]}");
    }
    return 0;
}
// loanstate: players whose loan record survives while the playing team is the contract team, i.e. what the game
// leaves behind when a loan is over (read-only).
int LoanStateCommand()
{
    using var session = Attach(writable: false);
    var memory = session.Memory;
    var profile = session.Profile;
    var deals = session.TransferDeals;
    if (profile.Team is not { } t || profile.Contract is not { } c ||
        profile.FutureTransfers is not { CurrentLoanHolder: { } holderOffset } || profile.FutureTransfers is not { } ft) return 2;

    bool TryQ(ulong at, out ulong value)
    {
        Span<byte> b = stackalloc byte[8];
        value = 0;
        return at != 0 && memory.TryRead(at, b) && (value = BitConverter.ToUInt64(b)) != 0;
    }
    bool IsTeam(ulong team) => TryQ(team, out var vt) && vt == session.ModuleBase + t.VtableRva;
    bool IsContract(ulong contract) => TryQ(contract, out var vt) && vt == session.ModuleBase + c.ContractVtableRva;
    static DateOnly? DateAt(byte[] b, int at)
    {
        if (b.Length < at + 4) return null;
        int day = BitConverter.ToInt16(b, at) & 0x1FF, year = BitConverter.ToInt16(b, at + 2);
        if (year <= 1900 || year > 2300 || day < 1 || day > 366) return null;
        var d = new DateOnly(year, 1, 1).AddDays(day - 1);
        return d.Year == year ? d : null;
    }

    var all = session.Players.LoadAll();
    var today = session.ReadGameDate();
    int withHolder = 0, sameTeam = 0, expiredSameTeam = 0, different = 0, broken = 0, shown = 0;
    foreach (var p in all)
    {
        TryQ(p.Address + (ulong)holderOffset, out var holder);
        if (holder == 0) continue;
        withHolder++;
        TryQ(holder, out var loan);
        TryQ(p.Address + (ulong)c.PlayerContract, out var contract);
        if (loan == 0 || contract == 0 || !IsContract(contract)) { broken++; continue; }
        TryQ(contract + (ulong)c.Team, out var parent);
        TryQ(p.Address + (ulong)t.PlayerTeam, out var playing);
        if (!IsTeam(parent)) { broken++; continue; }
        var raw = memory.Read(loan, Math.Max(ft.Expires + 4, 0xD4));
        var expires = DateAt(raw, ft.Expires);
        bool same = playing == parent;
        if (same)
        {
            sameTeam++;
            if (expires is { } e && today is { } d && e < d) expiredSameTeam++;
            if (shown++ < 20)
                Console.WriteLine($"{p.Id} {p.DisplayName}: parent {deals.TeamName(parent)} playing {deals.TeamName(playing)} expires {expires:yyyy-MM-dd} club {p.ClubName}");
        }
        else different++;
    }
    Console.WriteLine($"holders {withHolder:N0}: playing == contract team {sameTeam:N0} (expired {expiredSameTeam:N0}); different {different:N0}; broken {broken:N0}; today {today:yyyy-MM-dd}");
    return 0;
}

// loanraw: every pointer and flag a loan recall would touch, for one player (read-only).
int LoanRawCommand(int id)
{
    using var session = Attach(writable: false);
    var player = session.Players.LoadAll().Single(p => p.Id == id);
    var memory = session.Memory;
    var profile = session.Profile;
    var deals = session.TransferDeals;
    if (profile.Team is not { } t || profile.Contract is not { } c || profile.Relations is not { } rel)
    {
        Console.WriteLine("profile lacks team/contract/relations offsets");
        return 2;
    }

    bool TryQ(ulong at, out ulong value)
    {
        Span<byte> b = stackalloc byte[8];
        value = 0;
        return at != 0 && memory.TryRead(at, b) && (value = BitConverter.ToUInt64(b)) != 0;
    }
    string Name(ulong team) => team == 0 ? "-" : deals.TeamName(team) ?? "?";
    void Line(string what, ulong address, int offset)
    {
        ulong at = address + (ulong)offset;
        bool ok = TryQ(at, out var v);
        Console.WriteLine($"  {what,-24} +0x{offset:X3} @0x{at:X} = {(ok ? $"0x{v:X} {Name(v)}" : "0")}");
    }
    int IndexIn(ulong team, out int count)
    {
        count = 0;
        if (team == 0 || !TryQ(team + (ulong)t.Squad, out var begin) || !TryQ(team + (ulong)t.Squad + 8, out var end)
            || end < begin || (end - begin) % 8 != 0 || (end - begin) / 8 > 200) return -1;
        var raw = memory.Read(begin, (int)(end - begin));
        count = raw.Length / 8;
        for (int i = 0; i < count; i++) if (BitConverter.ToUInt64(raw, i * 8) == player.Address) return i;
        return -1;
    }

    Console.WriteLine($"{player.Id} {player.DisplayName} at 0x{player.Address:X}; module 0x{session.ModuleBase:X}");
    Console.WriteLine($"club shown {player.ClubName}; contract {player.Contract?.Wage} wage {player.Contract?.Expires:yyyy-MM-dd}");
    Line("playing team", player.Address, t.PlayerTeam);
    Line("player +0x138", player.Address, t.PlayerTeam + 8);
    Line("player +0x30", player.Address, 0x30);
    Line("person", player.Address, rel.PlayerPerson);
    Line("contract", player.Address, c.PlayerContract);
    Line("player +0x390", player.Address, 0x390);
    TryQ(player.Address + (ulong)c.PlayerContract, out var contract);
    ulong playing = TryQ(player.Address + (ulong)t.PlayerTeam, out var pt) ? pt : 0;
    if (contract != 0)
    {
        Console.WriteLine($"  contract 0x{contract:X} vtable 0x{(TryQ(contract, out var cv) ? cv : 0):X}");
        Line("contract team", contract, c.Team);
        Line("contract senior team", contract, c.SeniorTeam ?? 0x18);
        TryQ(contract + (ulong)c.Team, out var ct);
        Console.WriteLine($"  parent team {Name(ct)} 0x{ct:X}; playing team {Name(playing)} 0x{playing:X}");
        Console.WriteLine($"  in parent squad: {IndexIn(ct, out var pn)} of {pn}; in playing squad: {IndexIn(playing, out var bn)} of {bn}");
    }
    if (profile.FutureTransfers is { CurrentLoanHolder: { } holderOffset })
    {
        ulong slot = player.Address + (ulong)holderOffset;
        TryQ(slot, out var holder);
        Console.WriteLine($"  loan holder slot +0x{holderOffset:X3} @0x{slot:X} = 0x{holder:X}; holder[0] = 0x{(holder != 0 && TryQ(holder, out var hc) ? hc : 0):X}");
        if (holder != 0 && TryQ(holder, out var loanContract) && loanContract != 0)
        {
            Console.WriteLine($"  loan contract 0x{loanContract:X} vtable 0x{(TryQ(loanContract, out var lv) ? lv - session.ModuleBase : 0):X}");
            Line("loan team", loanContract, 0x10);
            Line("loan +0x18", loanContract, 0x18);
            var raw = memory.Read(loanContract, 0xD4);
            Console.WriteLine($"  agreed {BitConverter.ToInt32(raw, 0xC0)}; buyback {BitConverter.ToInt32(raw, 0xC8)}; " +
                $"monthly {BitConverter.ToInt32(raw, 0xCC)}; unused {BitConverter.ToInt32(raw, 0xD0)}");
            Console.WriteLine($"  loan bytes c0..d4 {Convert.ToHexString(raw[0xC0..0xD4])}");
        }
    }
    var loan = session.ReadCurrentLoan(player);
    Console.WriteLine($"  current loan: {loan.Status}; borrowing 0x{loan.BorrowingTeamAddress:X}; signed {loan.Signed:yyyy-MM-dd}; expires {loan.Contract?.Expires:yyyy-MM-dd}");
    return loan.Status == CurrentLoanStatus.Present ? 0 : 1;
}

int SetWageById(int id, int value)
{
    using var session = Attach(writable: true);
    var player = session.Players.LoadAll().Single(p => p.Id == id);
    int before = player.Contract?.Wage ?? throw new StaleObjectException("Player has no readable contract");
    session.Contracts.SetWage(player, value);
    var after = session.Players.Reload(player.Address)?.Contract ?? throw new StaleObjectException("Player contract no longer readable");
    Console.WriteLine($"{id} {player.DisplayName}: wage {before} -> {after.Wage}");
    return 0;
}

int Verify(string goldenPath)
{
    using var session = Attach(writable: false);
    var players = session.Players.LoadAll();
    int failed = 0;
    foreach (var g in GoldenPlayers.Load(goldenPath))
    {
        var candidates = players.Where(p => p.DisplayName == g.Name && (g.DateOfBirth is null || p.DateOfBirth == g.DateOfBirth)).ToList();
        if (candidates.Count != 1)
        {
            Console.WriteLine($"FAIL {g.Name}: {candidates.Count} players with this name and birth date");
            failed++;
            continue;
        }
        var diffs = GoldenPlayers.Compare(g, candidates[0]);
        Console.WriteLine(diffs.Count == 0
            ? $"ok   {g.Name} ({g.Attributes.Count} attributes, CA {candidates[0].CurrentAbility} PA {candidates[0].PotentialAbility})"
            : $"FAIL {g.Name}: {string.Join("; ", diffs)}");
        if (diffs.Count > 0) failed++;
    }
    Console.WriteLine(failed == 0 ? "all golden players match" : $"{failed} golden players differ");
    return failed == 0 ? 0 : 3;
}

// release <player id> [--write]   release preview; --write applies it
int ReleasePlayer(int id, bool write)
{
    using var session = Attach(writable: write);
    var players = session.Players.LoadAll().Where(p => p.Id == id).ToArray();
    if (players.Length != 1) { Console.Error.WriteLine($"{players.Length} players with id {id}"); return 2; }
    var service = new Fm27Editor.Core.Transfer.ReleaseService(session.Memory, session.Profile, session.ModuleBase,
        () => session.Staff.LoadAll());
    var plan = service.Plan(players[0]);
    Console.WriteLine($"{id} {players[0].DisplayName}: {players[0].ClubName} -> free agent, {plan.Squads} squads, {plan.Captaincies} captaincies, {plan.Writes.Count} writes");
    if (!write) { Console.WriteLine("plan only; add --write to apply"); return 0; }
    service.Apply(plan);
    var after = session.Players.Reload(players[0].Address) ?? throw new StaleObjectException("Player no longer readable");
    Console.WriteLine($"applied; club {after.ClubName}, contract {(after.Contract is null ? "none" : "present")}");
    return 0;
}

// releaseinspect <player id>   read-only examination of the player's team and captaincy links
int ReleaseInspect(int id)
{
    using var session = Attach(writable: false);
    var players = session.Players.LoadAll().Where(p => p.Id == id).ToArray();
    if (players.Length != 1) { Console.Error.WriteLine($"{players.Length} players with id {id}"); return 2; }
    var inspection = new Fm27Editor.Core.Transfer.ReleaseInspector(session.Memory, session.Profile, session.ModuleBase).Inspect(players[0]);
    Console.WriteLine($"{id} {players[0].DisplayName}: {players[0].ClubName}; contract 0x{inspection.Contract:X}; playing team 0x{inspection.PlayingTeam:X}");
    foreach (var team in inspection.Teams)
        Console.WriteLine($"  team 0x{team.Address:X}: squad {team.SquadCount}, player {team.PlayerOccurrences}, captain {team.Captain}, vice {team.ViceCaptain}");
    Console.WriteLine("read only; managed-club and other release conditions not checked");
    return 0;
}

// move <name> <buyer club id> <fee> [--write]   permanent transfer; without --write only prints the plan
int Move(string name, int buyerId, int fee, bool write)
{
    using var session = Attach(writable: write);
    var all = session.Players.LoadAll();
    var matches = all.Where(p => int.TryParse(name, out int id) ? p.Id == id : p.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
    if (matches.Count != 1) { Console.Error.WriteLine($"{matches.Count} players named '{name}'"); return 2; }
    var sw = Stopwatch.StartNew();
    var clubs = session.LoadClubs(all);
    Console.WriteLine($"{clubs.Count} clubs, {clubs.Sum(c => c.Teams.Count)} teams in {sw.ElapsedMilliseconds} ms");
    var buyer = clubs.SingleOrDefault(c => c.Id == buyerId);
    if (buyer is null) { Console.Error.WriteLine($"no club with id {buyerId}"); return 2; }
    var plan = session.Transfers.Plan(matches[0], clubs, buyer, fee);
    Console.WriteLine($"{matches[0].DisplayName}: {plan.Seller.DisplayName} -> {plan.Buyer.DisplayName} for {fee}, wage {plan.Wage}");
    Console.WriteLine($"into team 0x{plan.ToTeam.Address:X} ({plan.ToTeam.SquadCount}/{plan.ToTeam.SquadCapacity}{(plan.ToTeam.IsFirstTeam ? ", first team" : "")})"
                      + (plan.NeedsAllocation ? ", squad full: a new buffer will be allocated" : ""));
    foreach (var w in plan.Writes.Where(w => w.Value.Length == 4))
        Console.WriteLine($"  {w.What}: {BitConverter.ToInt32(w.Expected)} -> {BitConverter.ToInt32(w.Value)}");
    Console.WriteLine($"  {plan.Writes.Count(w => w.Value.Length == 8)} pointer writes");
    if (!write) { Console.WriteLine("plan only; add --write to apply"); return 0; }
    session.ApplyTransfer(plan);
    var after = session.Players.Reload(matches[0].Address)!;
    Console.WriteLine($"applied; contract club now {after.Club?.DisplayName}");
    return 0;
}

// clauses <club id>   sell-on / appearance fee / competition bonus clauses on the club's players
int Clauses(int clubId)
{
    using var session = Attach(writable: false);
    var all = session.Players.LoadAll();
    var club = session.LoadClubs(all).SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var byId = all.Where(p => p.Id is not null).GroupBy(p => p.Id!.Value).ToDictionary(g => g.Key, g => g.First());
    var clauses = session.ReadTransferClauses(club.Address, session.LoadCompetitions(all));
    Console.WriteLine($"{club.DisplayName}: {clauses.Count} clauses");
    foreach (var c in clauses)
        Console.WriteLine($"  {c.Kind,-16} {(byId.TryGetValue(c.PlayerId, out var p) ? p.DisplayName : c.PlayerId.ToString()),-24} {c.Beneficiary?.DisplayName,-24} " +
                          $"fee {c.Amount?.ToString() ?? "-"} pct {c.Percentage?.ToString() ?? "-"} signed for {c.SignedFor?.ToString() ?? "-"} apps {c.AppearancesSoFar}/{c.Appearances} competition {c.Competition?.DisplayName ?? c.CompetitionIndex?.ToString()} ends {c.Ends:yyyy-MM-dd}");
    return 0;
}

// alloc <bytes>   check that the editor can allocate memory inside the game process
int AllocCommand(int bytes)
{
    using var session = Attach(writable: true);
    Console.WriteLine($"writable {session.CanWrite}");
    ulong address = session.Process.Allocate(bytes);
    Console.WriteLine($"allocated {bytes} bytes at 0x{address:X}");
    return 0;
}

// income <club id> [set <period> <income|expenditure> <field> <value> [--write]]
int IncomeCommand(int clubId, string[] rest)
{
    bool write = rest.Contains("--write");
    using var session = Attach(writable: write);
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var periods = session.FinancialStatements.Read(club.Address);
    Console.WriteLine($"{club.Name}: {periods.Count} statement periods");
    foreach (var p in periods)
        Console.WriteLine($"  period {p.Index}: income {p.TotalIncome,14:N0}  expenditure {p.TotalExpenditure,14:N0}  " +
                          $"net {p.TotalIncome - p.TotalExpenditure,14:N0}  (gates {p.GateReceipts:N0}, wages {p.PlayerWages:N0})");
    if (periods.Count > 0)
    {
        var p = periods[0];
        Console.WriteLine("  current block detail:");
        Console.WriteLine($"    income      gate {p.GateReceipts:N0} other {p.OtherIncome:N0} season {p.SeasonTickets:N0} interest {p.Interest:N0} " +
                          $"invest {p.Investments:N0} merch {p.Merchandising:N0} sold {p.PlayersSold:N0} sponsor {p.Sponsorship:N0} " +
                          $"matchday {p.MatchDayIncome:N0} grants {p.Grants:N0}");
        Console.WriteLine($"    expenditure other {p.Other:N0} loan {p.LoanRepayment:N0} fines {p.LeagueFines:N0} ground {p.GroundMaintenance:N0} " +
                          $"playerWages {p.PlayerWages:N0} staffWages {p.StaffWages:N0} bonuses {p.Bonuses:N0} loyalty {p.LoyaltyBonuses:N0} " +
                          $"transfers {p.TransferExpenditure:N0} vat {p.Vat:N0} ni {p.NiEmployerTax:N0}");
    }

    if (rest.Length == 0) return 0;
    if (rest[0] != "set" || rest.Length < 5)
    {
        Console.Error.WriteLine("usage: income <club-id> [set <period> <income|expenditure> <field> <value> [--write]]");
        return 2;
    }
    int period = int.Parse(rest[1]);
    bool isIncome = rest[2].StartsWith("inc", StringComparison.OrdinalIgnoreCase);
    string field = rest[3];
    long value = long.Parse(rest[4]);
    var all = isIncome ? ClubStatementOffsets.IncomeKeys : ClubStatementOffsets.ExpenditureKeys;
    if (!all.Contains(field)) { Console.Error.WriteLine($"unknown field; one of {string.Join(", ", all)}"); return 2; }
    if (period < 0 || period >= periods.Count) { Console.Error.WriteLine($"period must be 0..{periods.Count - 1}"); return 2; }
    var current = CurrentValue(periods[period], isIncome, field);
    Console.WriteLine($"{(isIncome ? "income" : "expenditure")}.{field} period {period}: {current:N0} -> {value:N0}");
    if (!write) { Console.WriteLine("plan only; add --write to apply"); return 0; }
    session.FinancialStatements.Set(club.Address, period, isIncome, field, current, value);
    var after = session.FinancialStatements.Read(club.Address);
    var readBack = CurrentValue(after[period], isIncome, field);
    Console.WriteLine($"written; read back {readBack:N0}" + (readBack == value ? " (matches)" : " (MISMATCH)"));
    return readBack == value ? 0 : 1;
}

static long CurrentValue(ClubFinancialPeriod p, bool income, string field) => (income, field) switch
{
    (true, "gateReceipts") => p.GateReceipts,
    (true, "other") => p.OtherIncome,
    (true, "seasonTickets") => p.SeasonTickets,
    (true, "interest") => p.Interest,
    (true, "investments") => p.Investments,
    (true, "merchandising") => p.Merchandising,
    (true, "playersSold") => p.PlayersSold,
    (true, "sponsorship") => p.Sponsorship,
    (true, "matchDayIncome") => p.MatchDayIncome,
    (true, "grants") => p.Grants,
    (false, "other") => p.Other,
    (false, "loanRepayment") => p.LoanRepayment,
    (false, "leagueFines") => p.LeagueFines,
    (false, "groundMaintenance") => p.GroundMaintenance,
    (false, "playerWages") => p.PlayerWages,
    (false, "staffWages") => p.StaffWages,
    (false, "bonuses") => p.Bonuses,
    (false, "loyaltyBonuses") => p.LoyaltyBonuses,
    (false, "transferExpenditure") => p.TransferExpenditure,
    (false, "vat") => p.Vat,
    (false, "niEmployerTax") => p.NiEmployerTax,
    _ => throw new ArgumentOutOfRangeException(nameof(field)),
};

// financeprobe <id>   dump the club finance object and the FM Studio candidate statement offsets
int FinanceProbeCommand(int clubId)
{
    using var session = Attach(writable: false);
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var memory = session.Memory;
    ulong Pointer(ulong address)
    {
        var b = memory.Read(address, 8);
        return b.Length < 8 ? 0 : BitConverter.ToUInt64(b, 0);
    }
    ulong finance = Pointer(club.Address + 0x150);
    Console.WriteLine($"{club.Name}: club 0x{club.Address:X} finance 0x{finance:X}");
    Console.WriteLine($"  balance {session.ClubFields.Read(club.Address, Fm27Editor.Core.Clubs.ClubField.Balance)}");
    if (finance == 0) return 0;
    foreach (int offset in new[] { 0x280, 0x31C, 0x398, 0x434, 0x4B0, 0x560, 0x568, 0x5C8, 0x604, 0x664 })
    {
        var raw = memory.Read(finance + (ulong)offset, 8);
        if (raw.Length < 8) continue;
        Console.WriteLine($"  +0x{offset:X3}  i32 {BitConverter.ToInt32(raw, 0),12:N0}  u64 0x{BitConverter.ToUInt64(raw, 0):X}");
    }
    Console.WriteLine("  non-trivial int32 in finance+0x200..0x700:");
    for (int offset = 0x200; offset < 0x700; offset += 4)
    {
        var raw = memory.Read(finance + (ulong)offset, 4);
        if (raw.Length < 4) break;
        int value = BitConverter.ToInt32(raw, 0);
        if (value is < 1000 and > -1000) continue;
        Console.WriteLine($"    +0x{offset:X3} {value,14:N0}");
    }
    return 0;
}

// clubfinancelists <id>   Finances sponsors and debts read from the live game
int ClubFinanceListsCommand(int clubId){
    using var session = Attach(writable: false);
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var sponsors = session.ReadSponsors(club.Address);
    Console.WriteLine($"{club.Name}: {sponsors.Count} sponsors");
    foreach (var s in sponsors) Console.WriteLine($"  type {s.IncomeTypeCode,3} {s.TotalValue,12:N0} {s.StartDate:yyyy-MM-dd} {s.EndDate:yyyy-MM-dd}");
    var debts = session.ReadDebts(club.Address);
    Console.WriteLine($"{debts.Count} debts");
    foreach (var d in debts) Console.WriteLine($"  source {d.SourceCode,3} {d.OriginalDebt,12:N0} {d.StartDate:yyyy-MM-dd} {d.EndDate:yyyy-MM-dd}");
    return 0;
}

// clubsponsor <id> <index> <total|dates> <value[|start,end]> [--write]   guarded edit of one existing sponsor record
int ClubSponsorCommand(string[] args)
{
    bool write = args.Contains("--write");
    var words = args.Where(a => a != "--write").ToArray();
    if (words.Length >= 2 && words[1].Equals("add", StringComparison.OrdinalIgnoreCase))
        return ClubSponsorAdd(words[0], words[2..], write);
    if (words.Length >= 3 && words[1].Equals("remove", StringComparison.OrdinalIgnoreCase))
        return ClubSponsorRemove(words[0], words[2], write);
    if (words.Length < 4) throw new ArgumentException("usage: clubsponsor <club-id> <index> <total|dates> <value|start,end> [--write] | add <type> <total> <start,end> | remove <index>");
    int clubId = int.Parse(words[0]), index = int.Parse(words[1]);
    using var session = Attach(writable: write);
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var sponsors = session.ReadSponsors(club.Address);
    if (index < 0 || index >= sponsors.Count) { Console.Error.WriteLine($"club has {sponsors.Count} sponsors"); return 2; }
    var entry = sponsors[index];
    Console.WriteLine($"{club.Name} sponsor {index}: type {entry.IncomeTypeCode} total {entry.TotalValue:N0} {entry.StartDate:yyyy-MM-dd}..{entry.EndDate:yyyy-MM-dd} renew {entry.RenewIncome} fixed {entry.FixedValue}");
    if (!write) return 0;
    if (words[2].Equals("total", StringComparison.OrdinalIgnoreCase))
        session.SponsorWriter.SetTotal(club.Address, entry, int.Parse(words[3]));
    else if (words[2].Equals("dates", StringComparison.OrdinalIgnoreCase))
    {
        var parts = words[3].Split(',');
        if (parts.Length != 2) throw new ArgumentException("dates takes start,end as yyyy-MM-dd");
        session.SponsorWriter.SetDates(club.Address, entry,
            DateOnly.ParseExact(parts[0], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            DateOnly.ParseExact(parts[1], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
    }
    else if (words[2].Equals("flags", StringComparison.OrdinalIgnoreCase))
    {
        var flags = words[3].Split(",");
        if (flags.Length != 2) throw new ArgumentException("flags takes renew,fixed as 0/1");
        session.SponsorWriter.SetFlags(club.Address, entry, flags[0] == "1", flags[1] == "1");
    }
    else if (words[2].Equals("income", StringComparison.OrdinalIgnoreCase))
        session.SponsorWriter.SetIncomeType(club.Address, entry, byte.Parse(words[3]));
    else throw new ArgumentException("field must be total, dates, income or flags");
    var after = session.ReadSponsors(club.Address)[index];
    Console.WriteLine($"read back: type {after.IncomeTypeCode} total {after.TotalValue:N0} {after.StartDate:yyyy-MM-dd}..{after.EndDate:yyyy-MM-dd} renew {after.RenewIncome} fixed {after.FixedValue}");
    return 0;
}

// setclub <id> <ClubField> <value>   guarded write of one mapped club field (team fields go to the first team)
int SetClubField(int clubId, string fieldName, long value)
{
    var field = Enum.Parse<Fm27Editor.Core.Clubs.ClubField>(fieldName, ignoreCase: true);
    using var session = Attach(writable: true);
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var access = session.ClubFields;
    var root = session.Profile.Club!.Fields![field].Root == Fm27Editor.Core.Clubs.ClubFieldRoot.Club ? club.Address : club.FirstTeam?.Address ?? 0;
    var before = access.Read(root, field) ?? throw new InvalidOperationException($"{field} is not readable for {club.Name}");
    access.Set(root, field, before, value);
    Console.WriteLine($"{club.Name} {field}: {before} -> {access.Read(root, field)}");
    return 0;
}

// byid <id>...   name, club and jadedness/condition of players by unique ID
// peek <player|contract> <hexoffset> <size> [ids...]   value histogram over every player, plus the listed players (read only)
int Peek(string root, int offset, int size, int[] ids)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var counts = new Dictionary<long, int>();
    var buf = new byte[8];
    foreach (var p in session.Players.LoadAll())
    {
        ulong at = p.Address;
        if (root == "contract")
        {
            if (!mem.TryRead(p.Address + 0x330, buf) || BitConverter.ToUInt64(buf) is var c && c == 0) continue;
            at = c;
        }
        if (!mem.TryRead(at + (ulong)offset, buf.AsSpan(0, size))) continue;
        long v = size switch { 1 => (sbyte)buf[0], 2 => BitConverter.ToInt16(buf), 4 => BitConverter.ToInt32(buf), _ => BitConverter.ToInt64(buf) };
        counts[v] = counts.GetValueOrDefault(v) + 1;
        if (p.Id is { } id && ids.Contains(id)) Console.WriteLine($"  {id,-11} {p.DisplayName,-26} {p.Club?.DisplayName,-24} {v}");
    }
    Console.WriteLine($"{root}+0x{offset:X} ({size}): " + string.Join("  ", counts.OrderByDescending(k => k.Value).Take(12).Select(k => $"{k.Key}×{k.Value}")));
    return 0;
}

// unh <club name>   unhappiness motives of each first-team player (read only); unh all = how many players have active ones
int UnhCommand(string clubName)
{
    using var session = Attach(writable: false);
    var today = session.ReadGameDate() ?? throw new InvalidOperationException("The in-game date could not be read");
    Console.WriteLine($"game date {today:yyyy-MM-dd}");
    var players = session.Players.LoadAll();
    if (clubName == "all")
    {
        int withList = 0, active = 0;
        foreach (var pl in players)
        {
            var m = session.ReadUnhappiness(pl);
            if (m.Count > 0) withList++;
            if (m.Any(x => x.IsActive(today))) active++;
        }
        Console.WriteLine($"{withList} players with motives, {active} with active ones");
        return 0;
    }
    var club = session.LoadClubs(players).FirstOrDefault(c => c.Name.Equals(clubName, StringComparison.OrdinalIgnoreCase));
    if (club?.FirstTeam is not { } team) { Console.Error.WriteLine($"no club or first team named {clubName}"); return 2; }
    foreach (var p in session.ReadSquad(team.Address))
        foreach (var m in session.ReadUnhappiness(p))
            Console.WriteLine($"  {p.Id,-11} {p.DisplayName,-24} motive {m.Motive} type {m.Type} from {m.Start:yyyy-MM-dd} to {m.End:yyyy-MM-dd} items {m.Item1}/{m.Item2} {(m.IsActive(today) ? "ACTIVE" : "ended")}");
    return 0;
}

// removeunh <name>   remove the active unhappiness motives (writes to the game)
int RemoveUnhCommand(string name)
{
    using var session = Attach(writable: true);
    if (OnePlayer(session, name) is not { } p) return 2;
    var today = session.ReadGameDate()!.Value;
    void Show(string when) => Console.WriteLine($"{when}: " + string.Join("; ", session.ReadUnhappiness(p).Select(m => $"motive {m.Motive} end {m.End:yyyy-MM-dd} {(m.IsActive(today) ? "ACTIVE" : "ended")}")));
    Show("before");
    Console.WriteLine($"removed {session.RemoveUnhappiness(p)}");
    Show("after ");
    return 0;
}

// match <player|contract> <size> <id>=<value>...   offsets where every listed player holds the given value (read only);
//   size 0 = game date (int16 day of year in the low 9 bits + int16 year), value yyyy-MM-dd
int MatchCommand(string root, int size, string[] pairs)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var want = pairs.Select(s => s.Split('=')).ToDictionary(a => int.Parse(a[0]),
        a => size == 0 && DateOnly.Parse(a[1]) is var d ? d.Year * 1000L + d.DayOfYear : long.Parse(a[1]));
    if (size == 0) size = -4;
    int span = root == "contract" ? 0x100 : 0x3C8;
    var buf = new byte[8];
    var blocks = new Dictionary<int, byte[]>();
    foreach (var p in session.Players.LoadAll().Where(p => p.Id is { } id && want.ContainsKey(id)))
    {
        ulong at = p.Address;
        if (root == "contract" && (!mem.TryRead(p.Address + 0x330, buf) || (at = BitConverter.ToUInt64(buf)) == 0)) continue;
        var b = new byte[span];
        if (mem.TryRead(at, b)) blocks[p.Id!.Value] = b;
    }
    Console.WriteLine($"{blocks.Count}/{want.Count} players read");
    for (int o = 0; o + Math.Abs(size) <= span; o++)
        if (blocks.All(kv => (size switch
            {
                1 => (sbyte)kv.Value[o], 2 => BitConverter.ToInt16(kv.Value, o),
                -4 => BitConverter.ToInt16(kv.Value, o + 2) * 1000L + (BitConverter.ToUInt16(kv.Value, o) & 0x1FF),
                _ => BitConverter.ToInt32(kv.Value, o)
            }) == want[kv.Key]))
            Console.WriteLine($"  {root}+0x{o:X}");
    return 0;
}

// datediff <player|contract> <offset> <player|contract> <offset>   first players whose two dates differ (read only)
int DateDiffCommand(string rootA, int offA, string rootB, int offB)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var buf = new byte[8];
    DateOnly? At(PlayerRecord p, string root, int off)
    {
        ulong at = p.Address;
        if (root == "contract" && (!mem.TryRead(p.Address + 0x330, buf) || (at = BitConverter.ToUInt64(buf)) == 0)) return null;
        if (!mem.TryRead(at + (ulong)off, buf.AsSpan(0, 4))) return null;
        int day = BitConverter.ToUInt16(buf) & 0x1FF, year = BitConverter.ToInt16(buf, 2);
        return year > 1900 && year < 2300 && day is >= 1 and <= 365 ? new DateOnly(year, 1, 1).AddDays(day - 1) : null;
    }
    int shown = 0, differ = 0, total = 0;
    foreach (var p in session.Players.LoadAll().Where(p => p.Club?.DisplayName is "Man Utd" or "Arsenal" or "Chelsea" or "Liverpool" or "Real Madrid"))
    {
        var (a, b) = (At(p, rootA, offA), At(p, rootB, offB));
        total++;
        if (a == b) continue;
        differ++;
        if (shown++ < 12) Console.WriteLine($"  {p.Id,-11} {p.DisplayName,-24} {p.Club?.DisplayName,-14} {rootA}+0x{offA:X} {a:yyyy-MM-dd}  {rootB}+0x{offB:X} {b:yyyy-MM-dd}");
    }
    Console.WriteLine($"{differ}/{total} differ");
    return 0;
}

// pokedate <name> <player|contract> <offset> <yyyy-MM-dd>   RE probe: write one date, keeping the flag bits (writes to the game)
int PokeDateCommand(string name, string root, int off, DateOnly date)
{
    using var session = Attach(writable: true);
    if (OnePlayer(session, name) is not { } p) return 2;
    var mem = session.Memory;
    if (!session.Profile.PlayerVtableRvas.Any(r => mem.ReadUInt64(p.Address) == session.Game.ModuleBase + r)) throw new InvalidOperationException("not a player object");
    ulong at = root == "contract" ? mem.ReadUInt64(p.Address + 0x330) : p.Address;
    if (root == "contract" && mem.ReadUInt64(at) != session.Game.ModuleBase + session.Profile.Contract!.ContractVtableRva) throw new InvalidOperationException("not a full contract");
    ulong addr = at + (ulong)off;
    var old = mem.Read(addr, 4);
    short raw = BitConverter.ToInt16(old);
    byte[] next = [.. BitConverter.GetBytes((short)((raw & ~0x1FF) | date.DayOfYear)), .. BitConverter.GetBytes((short)date.Year)];
    if (!mem.TryWrite(addr, next) || !mem.Read(addr, 4).AsSpan().SequenceEqual(next)) throw new InvalidOperationException("write failed");
    Console.WriteLine($"{p.DisplayName} {root}+0x{off:X}: {Convert.ToHexString(old)} -> {Convert.ToHexString(next)}");
    return 0;
}

// findperson <ca> <pa> [<ca> <pa>...]   RE: heap hits of adjacent int16 CA/PA, with the (distance, vtable) of module
//   pointers before each hit; prints the (distance, vtable) pairs every person shares (read only)
int FindPersonCommand(int[] pairs)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    ulong mb = session.Game.ModuleBase, me = session.Game.ModuleEnd;
    var sets = new List<HashSet<(int, ulong)>>();
    var hitsPer = new List<List<ulong>>();
    for (int i = 0; i < pairs.Length; i += 2) { sets.Add([]); hitsPer.Add([]); }
    var sw = Stopwatch.StartNew();
    var chunk = new byte[1 << 20];
    foreach (var r in ((WindowsProcessMemory)mem).EnumerateWritableRegions(mb, me).Where(r => !r.IsModuleData))
        for (ulong off = 0; off < r.Size; off += (ulong)chunk.Length)
        {
            int len = (int)Math.Min((ulong)chunk.Length, r.Size - off);
            var span = chunk.AsSpan(0, len);
            if (!mem.TryRead(r.Start + off, span)) continue;
            for (int k = 0; k + 4 <= len; k += 2)
            {
                short ca = BitConverter.ToInt16(chunk, k), pa = BitConverter.ToInt16(chunk, k + 2);
                for (int j = 0; j < pairs.Length; j += 2)
                {
                    if (ca != pairs[j] || pa != pairs[j + 1]) continue;
                    ulong hit = r.Start + off + (ulong)k;
                    hitsPer[j / 2].Add(hit);
                    var before = new byte[0x800];
                    if (!mem.TryRead(hit - 0x800, before)) continue;
                    for (int d = 8; d <= 0x800; d += 8)
                    {
                        ulong q = BitConverter.ToUInt64(before, 0x800 - d);
                        if (q >= mb && q < me && (q - mb) > 0x4000000 && (q - mb) < 0x5100000) sets[j / 2].Add((d, q - mb));
                    }
                }
            }
        }
    for (int j = 0; j < sets.Count; j++) Console.WriteLine($"CA {pairs[j * 2]} PA {pairs[j * 2 + 1]}: {hitsPer[j].Count} hits");
    foreach (var hit in hitsPer[0].Take(8))
    {
        var before = new byte[0x800];
        if (!mem.TryRead(hit - 0x800, before)) continue;
        var ptrs = Enumerable.Range(1, 0x100).Select(i => (d: i * 8, q: BitConverter.ToUInt64(before, 0x800 - i * 8)))
            .Where(x => x.q >= mb && x.q < me).Take(4).Select(x => $"-0x{x.d:X}:+0x{x.q - mb:X}");
        Console.WriteLine($"  hit 0x{hit:X}  {string.Join("  ", ptrs)}");
    }
    var common = sets.Skip(1).Aggregate(new HashSet<(int, ulong)>(sets[0]), (a, s) => { a.IntersectWith(s); return a; });
    foreach (var (d, vt) in common.OrderBy(x => x.Item1).Take(30)) Console.WriteLine($"  object = hit - 0x{d:X}, vtable +0x{vt:X}");
    Console.WriteLine($"{sw.Elapsed.TotalSeconds:F1} s");
    return 0;
}

// hex <addr> <len>   raw dump, 16 bytes per row, module pointers marked (read only)
int HexCommand(ulong addr, int len)
{
    using var session = Attach(writable: false);
    var b = session.Memory.Read(addr, len);
    ulong mb = session.Game.ModuleBase, me = session.Game.ModuleEnd;
    for (int i = 0; i < len; i += 16)
    {
        var row = b[i..Math.Min(len, i + 16)];
        string marks = string.Join(" ", Enumerable.Range(0, row.Length / 8).Select(k => BitConverter.ToUInt64(row, k * 8))
            .Select(q => q >= mb && q < me ? $"[+0x{q - mb:X}]" : q is > 0x10000000000 and < 0x7FF000000000 ? "[ptr]" : ""));
        Console.WriteLine($"{addr + (ulong)i:X}  {Convert.ToHexString(row)}  {marks}");
    }
    return 0;
}

// findlist <vtable rva>   RE: module statics (or objects they point to) holding a begin/end vector of pointers whose
//   elements carry the vtable (read only)
int FindListCommand(ulong vtRva)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    ulong mb = session.Game.ModuleBase, me = session.Game.ModuleEnd, vt = mb + vtRva;
    var q8 = new byte[8];
    bool IsTarget(ulong p) => p != 0 && mem.TryRead(p, q8) && BitConverter.ToUInt64(q8) == vt;
    int Check(byte[] blk, int o)
    {
        ulong begin = BitConverter.ToUInt64(blk, o), end = BitConverter.ToUInt64(blk, o + 8);
        if (begin < 0x10000000000 || end <= begin || (end - begin) % 8 != 0 || (end - begin) / 8 > 200000) return 0;
        var first = new byte[64];
        if (!mem.TryRead(begin, first)) return 0;
        int ok = Enumerable.Range(0, 8).Count(i => IsTarget(BitConverter.ToUInt64(first, i * 8)));
        return ok >= 6 ? (int)((end - begin) / 8) : 0;
    }
    foreach (var r in ((WindowsProcessMemory)mem).EnumerateWritableRegions(mb, me).Where(r => r.IsModuleData))
    {
        var data = new byte[r.Size];
        if (!mem.TryRead(r.Start, data)) continue;
        for (int o = 0; o + 16 <= data.Length; o += 8)
        {
            if (Check(data, o) is var n && n > 0) Console.WriteLine($"  direct: module+0x{r.Start + (ulong)o - mb:X} begin/end, {n} entries");
            ulong p = BitConverter.ToUInt64(data, o);
            if (p < 0x10000000000 || p > 0x7FF000000000) continue;
            var blk = new byte[Environment.GetEnvironmentVariable("FINDLIST_SPAN") is { } sp ? Convert.ToInt32(sp, 16) : 0x100];
            if (!mem.TryRead(p, blk)) continue;
            for (int k = 0; k + 16 <= blk.Length; k += 8)
                if (Check(blk, k) is var m && m > 0) Console.WriteLine($"  [module+0x{r.Start + (ulong)o - mb:X}] + 0x{k:X} begin/end, {m} entries");
        }
    }
    return 0;
}

// vthist <root rva> <offset>   RE: vtable histogram of the pointer vector at [module + root] + offset (read only)
int VtHistCommand(ulong rootRva, int off)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    ulong mb = session.Game.ModuleBase;
    ulong obj = mem.ReadUInt64(mb + rootRva);
    ulong begin = mem.ReadUInt64(obj + (ulong)off), end = mem.ReadUInt64(obj + (ulong)off + 8);
    var ptrs = mem.Read(begin, (int)(end - begin));
    var hist = new Dictionary<ulong, int>();
    var q = new byte[8];
    for (int i = 0; i < ptrs.Length; i += 8)
    {
        ulong p = BitConverter.ToUInt64(ptrs, i);
        ulong vt = p != 0 && mem.TryRead(p, q) ? BitConverter.ToUInt64(q) - mb : 0;
        hist[vt] = hist.GetValueOrDefault(vt) + 1;
    }
    Console.WriteLine($"object 0x{obj:X}, {ptrs.Length / 8} entries");
    foreach (var (vt, n) in hist.OrderByDescending(k => k.Value).Take(10)) Console.WriteLine($"  vtable +0x{vt:X}: {n}");
    return 0;
}

// ptrvt <addr> <len>   RE: for each qword pointer in [addr, addr+len), the module vtable of the object it points to (read only)
int PtrVtCommand(ulong addr, int len)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    ulong mb = session.Game.ModuleBase, me = session.Game.ModuleEnd;
    var b = mem.Read(addr, len);
    var q = new byte[8];
    for (int o = 0; o + 8 <= len; o += 8)
    {
        ulong p = BitConverter.ToUInt64(b, o);
        if (p < 0x10000000000 || p > 0x7FF000000000 || !mem.TryRead(p, q)) continue;
        ulong vt = BitConverter.ToUInt64(q);
        Console.WriteLine($"  +0x{o:X3} -> 0x{p:X}  {(vt >= mb && vt < me ? $"vtable +0x{vt - mb:X}" : "")}");
    }
    return 0;
}

// staff <club name|staff name>   staff of a club (or one named person with every attribute) (read only)
int StaffCommand(string name)
{
    using var session = Attach(writable: false);
    var sw = Stopwatch.StartNew();
    var all = session.Staff.LoadAll();
    Console.WriteLine($"{all.Count} staff in {sw.ElapsedMilliseconds} ms");
    var atClub = all.Where(s => s.Club?.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase) == true || s.Club?.Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true).ToList();
    bool raw = Environment.GetEnvironmentVariable("STAFF_JOBRAW") == "1";
    foreach (var s in atClub)
    {
        string jr = "";
        if (raw && session.Memory.ReadUInt64(s.Address + 0xA8) is var job && job != 0) jr = Convert.ToHexString(session.Memory.Read(job + 0x18, 0x10));
        Console.WriteLine($"  {s.Id,-9} job {s.JobCode,3}  {s.DisplayName,-26} {s.DateOfBirth:yyyy-MM-dd} {s.Nationality?.DisplayName,-16} CA {s.CurrentAbility} PA {s.PotentialAbility} {jr}");
    }
    if (atClub.Count > 0) { Console.WriteLine($"{atClub.Count} at {name}"); return 0; }
    if (name == "humans")
        foreach (var h in all.Where(s => s.Kind.Manager))
            Console.WriteLine($"  0x{h.Address:X} id {h.Id} '{h.FirstName}' '{h.LastName}' '{h.FullName}' club {h.Club?.DisplayName} job {h.JobCode} CA {h.CurrentAbility} PA {h.PotentialAbility} manager [{string.Join(",", h.ManagerAttributes!)}]");
    foreach (var s in all.Where(s => s.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase) || s.FullName?.Equals(name, StringComparison.OrdinalIgnoreCase) == true))
    {
        Console.WriteLine($"{s.DisplayName} ({s.FullName}) id {s.Id} born {s.DateOfBirth:yyyy-MM-dd} {s.Nationality?.DisplayName} {s.Club?.DisplayName} job {s.JobCode}");
        Console.WriteLine($"  person 0x{s.Address:X} staff 0x{s.StaffAddress:X}");
        Console.WriteLine($"  rep {s.HomeReputation}/{s.CurrentReputation}/{s.WorldReputation} CA {s.CurrentAbility} PA {s.PotentialAbility}");
        Console.WriteLine("  " + string.Join("  ", Enum.GetValues<Fm27Editor.Core.Staff.StaffAttribute>().Select(a => $"{a} {s.Attribute(a)}")));
        Console.WriteLine("  " + string.Join("  ", Enum.GetValues<Fm27Editor.Core.Staff.StaffPersonality>().Select(t => $"{t} {s.Personality[(int)t]}")));
    }
    return 0;
}

// setstaff <full or display name> <StaffAttribute|CA|PA> <value>   guarded staff write (writes to the game)
int SetStaffCommand(string name, string field, int value)
{
    using var session = Attach(writable: true);
    var matches = session.Staff.LoadAll().Where(s => s.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
    if (matches.Count != 1) { Console.Error.WriteLine($"{matches.Count} staff named '{name}'"); return 2; }
    var st = matches[0];
    var w = session.StaffWriter;
    if (field.Equals("CA", StringComparison.OrdinalIgnoreCase)) w.SetAbility(st, value, st.PotentialAbility);
    else if (field.Equals("PA", StringComparison.OrdinalIgnoreCase)) w.SetAbility(st, st.CurrentAbility, value);
    else if (Enum.TryParse<Fm27Editor.Core.Staff.ManagerAttribute>(field, true, out var ma))
    {
        w.SetManagerAttribute(st, ma, value);
        Console.WriteLine($"{st.DisplayName} {ma}: {st.ManagerAttributes![(int)ma]} -> {session.Staff.Reload(st.Address)!.ManagerAttributes![(int)ma]}");
        return 0;
    }
    else w.SetAttribute(st, Enum.Parse<Fm27Editor.Core.Staff.StaffAttribute>(field, true), value);
    var after = session.Staff.Reload(st.Address)!;
    Console.WriteLine($"{st.DisplayName}: CA {st.CurrentAbility}->{after.CurrentAbility} PA {st.PotentialAbility}->{after.PotentialAbility}"
        + (Enum.TryParse<Fm27Editor.Core.Staff.StaffAttribute>(field, true, out var a) ? $" {a} {st.Attribute(a)}->{after.Attribute(a)}" : ""));
    return 0;
}

// nation <name> [<NationField> <value>]   every mapped field of one nation; with a field, a guarded write (writes to the game)
int NationCommand(string name, string? field, long? value)
{
    using var session = Attach(writable: field is not null);
    var all = session.Nations.LoadAll();
    var n = all.FirstOrDefault(x => x.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase));
    if (n is null) { Console.Error.WriteLine($"no nation '{name}' ({all.Count} nations)"); return 2; }
    var acc = session.NationFields;
    if (field is not null)
    {
        var f = Enum.Parse<Fm27Editor.Core.Nations.NationField>(field, true);
        long before = acc.Read(n.Address, f) ?? throw new InvalidOperationException($"{f} is not readable");
        acc.Set(n.Address, f, before, value!.Value);
        Console.WriteLine($"{n.DisplayName} {f}: {before} -> {acc.Read(n.Address, f)}");
        return 0;
    }
    Console.WriteLine($"{n.DisplayName} id {n.Id} 0x{n.Address:X} ({all.Count} nations)");
    foreach (var (f, v) in acc.ReadAll(n.Address)) Console.WriteLine($"  {f,-28} {v}");
    return 0;
}

// stadium <name> [<StadiumField> <value>]   one stadium with every mapped value; with a field, a guarded write (writes to the game)
// export csv|json <path>   current player snapshots, same columns as the app's Export button
// restore <export.json> <club>   write every exported attribute back for the players of one club (repairs a bad preset)
int RestoreCommand(string path, string club)
{
    using var session = Attach(writable: true);
    var live = session.Players.LoadAll();
    using var stream = File.OpenRead(path);
    var exported = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, System.Text.Json.JsonElement>>>(stream)
        ?? throw new InvalidOperationException("export is empty");
    var targets = exported.Where(p => p.TryGetValue("Club", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.String
        && string.Equals(c.GetString(), club, StringComparison.OrdinalIgnoreCase)).ToArray();
    if (targets.Length == 0) { Console.Error.WriteLine($"no exported player has club '{club}'"); return 2; }
    var byId = live.Where(p => p.Id is > 0).GroupBy(p => p.Id!.Value).ToDictionary(g => g.Key, g => g.First());
    int players = 0, attributes = 0, missing = 0;
    foreach (var row in targets)
    {
        if (!row.TryGetValue("Id", out var idValue) || !byId.TryGetValue(idValue.GetInt32(), out var player)) { missing++; continue; }
        foreach (var attribute in Enum.GetValues<Fm27Editor.Core.Players.PlayerAttribute>())
        {
            if (!row.TryGetValue($"Attribute.{attribute}", out var value) || value.ValueKind != System.Text.Json.JsonValueKind.Number) continue;
            int display = value.GetInt32();
            if (display < 1 || display > 20 || player.Attribute(attribute) == display) continue;
            session.Writer.SetAttribute(player, attribute, display);
            player = session.Players.Reload(player.Address) ?? throw new InvalidOperationException("player disappeared");
            attributes++;
        }
        players++;
    }
    Console.WriteLine($"restored {attributes:N0} attributes for {players} players of {club}" + (missing > 0 ? $", {missing} not found" : ""));
    return 0;
}
// tactics <hex-address>   decode a team's Tactical Understanding block (offsets measured from FMRTE)
int TacticsCommand(string address)
{
    using var session = Attach(writable: false);
    ulong block = Convert.ToUInt64(address.Replace("0x", ""), 16);
    var values = Fm27Editor.Core.Clubs.TacticalBlock.Read(session.Memory, block)
        ?? throw new InvalidOperationException($"no tactical block at 0x{block:X}");
    Console.WriteLine($"0x{block:X}: Passing Style {values.PassingStyle}, Build Up {values.BuildUp}, Chance Creation {values.ChanceCreation}, " +
        $"Line Of Engagement {values.LineOfEngagement}, Pressing Intensity {values.PressingIntensity}, Pressing Trap {values.PressingTrap}, Position/Role {values.PositionRole?.ToString() ?? "?"}");
    return 0;
}
int ExportCommand(string format, string path)
{
    using var session = Attach(writable: false);
    var players = session.Players.LoadAll();
    if (session.ReadGameDate() is { } gameDate)
        foreach (var player in players) player.GameDate = gameDate;
    using (var stream = File.Create(path))
    {
        if (format.Equals("json", StringComparison.OrdinalIgnoreCase)) Fm27Editor.Core.Players.PlayerExport.WriteJson(stream, players);
        else Fm27Editor.Core.Players.PlayerExport.WriteCsv(stream, players);
    }
    Console.WriteLine($"exported {players.Count:N0} players to {path}");
    return 0;
}
int StadiumColourCommand(string[] words)
{
    bool write = words.Contains("--write");
    var rest = words.Where(w => w != "--write").ToArray();
    if (rest.Length < 2) throw new ArgumentException("usage: stadiumcolour <name> <#AARRGGBB> [--write]");
    string name = string.Join(" ", rest[..^1]), hex = rest[^1];
    using var session = Attach(writable: write);
    var all = session.Stadiums.LoadAll();
    var found = all.Where(s => s.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
    if (found.Count == 0) { Console.Error.WriteLine($"no stadium named '{name}'"); return 2; }
    uint argb = uint.Parse(hex.TrimStart('#'), System.Globalization.NumberStyles.HexNumber);
    foreach (var s in found)
    {
        Console.WriteLine($"{s.DisplayName} seat colour #{s.SeatColour:X8}");
        if (!write) continue;
        session.StadiumWriter.SetSeatColour(s, argb);
        Console.WriteLine($"read back: #{session.Stadiums.Reload(s.Address)!.SeatColour:X8}");
    }
    return 0;
}
int StadiumCommand(string name, string? field, long? value)
{
    using var session = Attach(writable: field is not null);
    var sw = Stopwatch.StartNew();
    var all = session.Stadiums.LoadAll();
    var st = all.Where(s => s.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
    Console.WriteLine($"{all.Count} stadiums in {sw.ElapsedMilliseconds} ms, {st.Count} named '{name}'");
    var w = session.StadiumWriter;
    foreach (var s in st)
    {
        if (field is not null)
        {
            var f = Enum.Parse<Fm27Editor.Core.Stadiums.StadiumField>(field, true);
            long before = w.ReadFields(s.Address)[f];
            w.SetField(s.Address, f, before, value!.Value);
            Console.WriteLine($"{s.DisplayName} {f}: {before} -> {w.ReadFields(s.Address)[f]}");
            continue;
        }
        Console.WriteLine($"{s.DisplayName} id {s.Id} owner {s.Owner?.DisplayName} nearby {s.NearbyStadium} lat {s.Latitude} lon {s.Longitude} flags 0x{s.Flags:X2} addr 0x{s.Address:X}");
        foreach (var (f, v) in w.ReadFields(s.Address)) Console.WriteLine($"  {f,-24} {v}");
        foreach (var t in s.Stands) Console.WriteLine($"  stand {t.Index}: {t.Capacity,6} roof {t.HasRoof} seats {t.HasSeats} tunnel {t.HasTunnel}");
        Console.WriteLine($"  stands total {s.StandsTotal}");
    }
    return 0;
}

// poke <addr> <old hex> <new hex>   RE probe: write bytes only when memory still holds the old bytes (writes to the game)
int PokeCommand(ulong addr, string oldHex, string newHex)
{
    using var session = Attach(writable: true);
    byte[] old = Convert.FromHexString(oldHex), now = Convert.FromHexString(newHex);
    if (old.Length != now.Length) throw new ArgumentException("old and new must have the same length");
    if (!session.Memory.Read(addr, old.Length).AsSpan().SequenceEqual(old)) throw new InvalidOperationException($"0x{addr:X} no longer holds {oldHex}");
    if (!session.Memory.TryWrite(addr, now) || !session.Memory.Read(addr, now.Length).AsSpan().SequenceEqual(now)) throw new InvalidOperationException("write failed");
    Console.WriteLine($"0x{addr:X}: {oldHex} -> {newHex}");
    return 0;
}

// rootvecs <root rva>   RE: every begin/end pointer vector in the first 0x800 bytes of [module + rva], with the first element's vtable (read only)
int RootVecsCommand(ulong rva)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    ulong mb = session.Game.ModuleBase;
    ulong obj = mem.ReadUInt64(mb + rva);
    var b = mem.Read(obj, 0x800);
    for (int o = 0; o + 16 <= b.Length; o += 8)
    {
        ulong begin = BitConverter.ToUInt64(b, o), end = BitConverter.ToUInt64(b, o + 8);
        if (begin < 0x10000000000 || end <= begin || (end - begin) % 8 != 0 || (end - begin) / 8 > 2_000_000) continue;
        if (!mem.TryRead(begin, new byte[8]) || mem.ReadUInt64(begin) is var first && first == 0 || !mem.TryRead(first, new byte[8])) continue;
        ulong vt = mem.ReadUInt64(first);
        if (vt < mb || vt >= session.Game.ModuleEnd) continue;
        Console.WriteLine($"  +0x{o:X3}: {(end - begin) / 8,8} entries, first vtable +0x{vt - mb:X}");
    }
    return 0;
}

// vecbounds <addr inside a pointer vector> <vtable rva>   RE: walk both ways while entries carry the vtable (read only)
int VecBoundsCommand(ulong inside, ulong vtRva)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    ulong vt = session.Game.ModuleBase + vtRva;
    bool Ok(ulong slot) => mem.TryRead(slot, new byte[8]) && mem.ReadUInt64(slot) is var p && p != 0 && mem.TryRead(p, new byte[8]) && mem.ReadUInt64(p) == vt;
    ulong lo = inside, hi = inside;
    while (Ok(lo - 8)) lo -= 8;
    while (Ok(hi + 8)) hi += 8;
    Console.WriteLine($"begin 0x{lo:X} end 0x{hi + 8:X}: {(hi + 8 - lo) / 8} entries");
    return 0;
}

// near <a> <b>   RE: heap int32 a with int32 b within ±0x40, plus the closest module pointer (vtable) below (read only)
int NearCommand(int a, int b)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    ulong mb = session.Game.ModuleBase, me = session.Game.ModuleEnd;
    var chunk = new byte[1 << 20];
    int hits = 0;
    foreach (var r in ((WindowsProcessMemory)mem).EnumerateWritableRegions(mb, me).Where(r => !r.IsModuleData))
        for (ulong off = 0; off < r.Size; off += (ulong)chunk.Length)
        {
            int len = (int)Math.Min((ulong)chunk.Length, r.Size - off);
            if (!mem.TryRead(r.Start + off, chunk.AsSpan(0, len))) continue;
            for (int k = 0; k + 4 <= len; k += 4)
            {
                if (BitConverter.ToInt32(chunk, k) != a) continue;
                int lo = Math.Max(0, k - 0x40), hi = Math.Min(len - 4, k + 0x40);
                int j = -1;
                for (int t = lo; t <= hi; t += 4) if (BitConverter.ToInt32(chunk, t) == b) { j = t; break; }
                if (j < 0) continue;
                ulong hit = r.Start + off + (ulong)k;
                var before = new byte[0x400];
                string vt = "";
                if (mem.TryRead(hit - 0x400, before))
                    for (int d = 8; d <= 0x400; d += 8)
                        if (BitConverter.ToUInt64(before, 0x400 - d) is var q && q >= mb && q < me) { vt = $"vtable +0x{q - mb:X} at -0x{d:X}"; break; }
                Console.WriteLine($"  0x{hit:X} ({b} at {(j - k):+0;-0}) {vt}");
                if (++hits > 30) return 0;
            }
        }
    return 0;
}

// clubprobe <clubA> <clubB> <size> <a>:<b>...   RE: like nationprobe, starting from two club objects (read only)
int ClubProbeCommand(string nameA, string nameB, int size, string[] pairs)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var clubs = session.LoadClubs(session.Players.LoadAll());
    var a = clubs.FirstOrDefault(c => c.DisplayName == nameA); var b = clubs.FirstOrDefault(c => c.DisplayName == nameB);
    if (a is null || b is null) { Console.Error.WriteLine("club not found"); return 2; }
    Console.WriteLine($"{nameA} 0x{a.Address:X}  {nameB} 0x{b.Address:X}");
    var want = pairs.Select(s => s.Split(':')).Select(x => (A: double.Parse(x[0], System.Globalization.CultureInfo.InvariantCulture), B: double.Parse(x[1], System.Globalization.CultureInfo.InvariantCulture))).ToList();
    double V(byte[] d, int o) => size switch { 1 => d[o], 2 => BitConverter.ToInt16(d, o), 4 => BitConverter.ToInt32(d, o), -4 => BitConverter.ToSingle(d, o), -8 => BitConverter.ToDouble(d, o), _ => 0 };
    void Scan(string where, byte[] da, byte[] db)
    {
        for (int o = 0; o + Math.Abs(size) <= Math.Min(da.Length, db.Length); o++)
            foreach (var (x, y) in want)
                if (Math.Abs(V(da, o) - x) < 0.0001 * Math.Max(1, Math.Abs(x)) && Math.Abs(V(db, o) - y) < 0.0001 * Math.Max(1, Math.Abs(y))) Console.WriteLine($"  {where}+0x{o:X}: {x} / {y}");
    }
    var oa = mem.Read(a.Address, 0x400); var ob = mem.Read(b.Address, 0x400);
    for (int k = 0; k + 8 <= 0x400; k += 8)
    {
        ulong pa = BitConverter.ToUInt64(oa, k), pb = BitConverter.ToUInt64(ob, k);
        if (pa < 0x10000000000 || pb < 0x10000000000 || pa > 0x7FF000000000 || pb > 0x7FF000000000 || pa == pb) continue;
        var da = new byte[0x400]; var db = new byte[0x400];
        if (!mem.TryRead(pa, da) || !mem.TryRead(pb, db)) continue;
        Scan($"[club+0x{k:X}] (vt +0x{BitConverter.ToUInt64(da) - session.Game.ModuleBase:X})", da, db);
    }
    return 0;
}

// nationbytes <offset hex> <len> [names...]   RE: bytes at [nation+0x108]+offset for every nation (or the named ones) (read only)
int NationBytesCommand(int offset, int len, string[] names)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var hist = new Dictionary<string, int>();
    foreach (var n in session.Nations.LoadAll())
    {
        ulong team = mem.ReadUInt64(n.Address + 0x108);
        if (team == 0 || !mem.TryRead(team + (ulong)offset, new byte[len])) continue;
        string hex = Convert.ToHexString(mem.Read(team + (ulong)offset, len));
        hist[hex] = hist.GetValueOrDefault(hex) + 1;
        if (names.Length == 0 || names.Contains(n.DisplayName, StringComparer.OrdinalIgnoreCase)) if (names.Length > 0) Console.WriteLine($"  {n.DisplayName,-24} {hex}");
    }
    Console.WriteLine("histogram: " + string.Join("  ", hist.OrderByDescending(k => k.Value).Take(12).Select(k => $"{k.Key}×{k.Value}")));
    return 0;
}

// nationprobe <nameA> <nameB> <size> <a>:<b>...   RE: offsets where nation A holds a and nation B holds b, in the nation
//   object and one pointer hop away (same pointer offset in both) (read only)
int NationProbeCommand(string nameA, string nameB, int size, string[] pairs)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var nations = session.Players.LoadAll().Select(p => p.Nationality).Where(n => n is not null).DistinctBy(n => n!.Address).ToList();
    var a = nations.FirstOrDefault(n => n!.DisplayName == nameA); var b = nations.FirstOrDefault(n => n!.DisplayName == nameB);
    if (a is null || b is null) { Console.Error.WriteLine("nation not found"); return 2; }
    Console.WriteLine($"{nameA} 0x{a.Address:X}  {nameB} 0x{b.Address:X}  ({nations.Count} nations)");
    var want = pairs.Select(s => s.Split(':')).Select(x => (A: double.Parse(x[0], System.Globalization.CultureInfo.InvariantCulture), B: double.Parse(x[1], System.Globalization.CultureInfo.InvariantCulture))).ToList();
    double V(byte[] d, int o) => size switch { 1 => d[o], 2 => BitConverter.ToInt16(d, o), 4 => BitConverter.ToInt32(d, o), -4 => BitConverter.ToSingle(d, o), _ => 0 };
    void Scan(string where, byte[] da, byte[] db)
    {
        for (int o = 0; o + Math.Abs(size) <= Math.Min(da.Length, db.Length); o++)
            foreach (var (x, y) in want)
                if (Math.Abs(V(da, o) - x) < 0.01 && Math.Abs(V(db, o) - y) < 0.01) Console.WriteLine($"  {where}+0x{o:X}: {x} / {y}");
    }
    var oa = mem.Read(a.Address, 0x400); var ob = mem.Read(b.Address, 0x400);
    Scan("nation", oa, ob);
    for (int k = 0; k + 8 <= 0x400; k += 8)
    {
        ulong pa = BitConverter.ToUInt64(oa, k), pb = BitConverter.ToUInt64(ob, k);
        if (pa < 0x10000000000 || pb < 0x10000000000 || pa > 0x7FF000000000 || pb > 0x7FF000000000 || pa == pb) continue;
        var da = new byte[0x900]; var db = new byte[0x900];
        if (!mem.TryRead(pa, da) || !mem.TryRead(pb, db)) continue;
        Scan($"[nation+0x{k:X}]", da, db);
        if (k != 0x108) continue;
        for (int j = 0; j + 8 <= da.Length; j += 8)
        {
            ulong qa = BitConverter.ToUInt64(da, j), qb = BitConverter.ToUInt64(db, j);
            if (qa < 0x10000000000 || qb < 0x10000000000 || qa > 0x7FF000000000 || qb > 0x7FF000000000 || qa == qb) continue;
            var ea = new byte[0x200]; var eb = new byte[0x200];
            if (mem.TryRead(qa, ea) && mem.TryRead(qb, eb)) Scan($"[[nation+0x108]+0x{j:X}]", ea, eb);
        }
    }
    return 0;
}

// staffprobe [club id]   RE prototype: walk the person vector, pick staff (part = person - 0x100, human + 0x140) (read only)
int StaffProbeCommand(int club)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    ulong mb = session.Game.ModuleBase;
    ulong obj = mem.ReadUInt64(mb + 0x5703738);
    ulong begin = mem.ReadUInt64(obj + 0x88), end = mem.ReadUInt64(obj + 0x90);
    var ptrs = mem.Read(begin, (int)(end - begin));
    var q = new byte[8];
    string? Str(ulong p) { if (p == 0 || !mem.TryRead(p, q.AsSpan(0, 4))) return null; int n = BitConverter.ToInt32(q); return n is > 0 and < 200 ? System.Text.Encoding.UTF8.GetString(mem.Read(p + 4, n)) : null; }
    string? Entry(ulong e) => e != 0 && mem.TryRead(e, q) ? Str(BitConverter.ToUInt64(q)) : null;
    int pooled = 0, human = 0, atClub = 0;
    var clubHist = new Dictionary<int, int>();
    for (int i = 0; i < ptrs.Length; i += 8)
    {
        ulong p = BitConverter.ToUInt64(ptrs, i);
        if (p == 0 || !mem.TryRead(p, q)) continue;
        ulong vt = BitConverter.ToUInt64(q) - mb;
        ulong s;
        if (vt == 0x4D8BE78) { s = p - 0x100; pooled++; }
        else if (vt == 0x4CC08D8) { s = p + 0x140; human++; }
        else continue;
        var pb = mem.Read(p, 0x90);
        var sb = mem.Read(s, 0xE8);
        int cid = BitConverter.ToInt32(sb, 0xB8);
        clubHist[cid] = clubHist.GetValueOrDefault(cid) + 1;
        if (cid != club && vt != 0x4CC08D8) continue;
        atClub++;
        int day = BitConverter.ToUInt16(pb, 0x88) & 0x1FF, year = BitConverter.ToInt16(pb, 0x8A);
        if (vt == 0x4CC08D8)
        {
            var below = mem.Read(p - 0x100, 0xE0);
            Console.WriteLine($"  person 0x{p:X}: at -0x100 vt +0x{BitConverter.ToUInt64(below) - mb:X} rep {BitConverter.ToInt16(below, 0xD4)}/{BitConverter.ToInt16(below, 0xD6)}/{BitConverter.ToInt16(below, 0xD8)} CA {BitConverter.ToInt16(below, 0xDA)} PA {BitConverter.ToInt16(below, 0xDC)}; job 0x{BitConverter.ToUInt64(pb, 0x8):X}");
        }
        Console.WriteLine($"  {BitConverter.ToInt32(pb, 0xC),-10} {Entry(BitConverter.ToUInt64(pb, 0x50))} {Entry(BitConverter.ToUInt64(pb, 0x58)),-16} full '{Str(BitConverter.ToUInt64(pb, 0x40))}' born {year}/{day} rep {BitConverter.ToInt16(sb, 0xD4)}/{BitConverter.ToInt16(sb, 0xD6)}/{BitConverter.ToInt16(sb, 0xD8)} CA {BitConverter.ToInt16(sb, 0xDA)} PA {BitConverter.ToInt16(sb, 0xDC)} club {cid} vt +0x{vt:X} staffvt +0x{BitConverter.ToUInt64(sb) - mb:X}");
    }
    Console.WriteLine($"pooled {pooled}, human {human}, listed {atClub}; top clubs: " + string.Join("  ", clubHist.OrderByDescending(k => k.Value).Take(6).Select(k => $"{k.Key}×{k.Value}")));
    return 0;
}

int ById(int[] ids)
{
    using var session = Attach(writable: false);
    foreach (var p in session.Players.LoadAll().Where(p => p.Id is { } id && ids.Contains(id)))
    {
        var v = session.PlayerFields.ReadAll(p);
        Console.WriteLine($"{p.Id,-11} {p.DisplayName,-26} {p.Club?.DisplayName,-24} 0x{p.Address:X} jaded {v.GetValueOrDefault(Fm27Editor.Core.Players.PlayerField.Jadedness),5} cond {v.GetValueOrDefault(Fm27Editor.Core.Players.PlayerField.Condition),5} injuries {session.ReadInjuries(p).Count}");
    }
    return 0;
}

// statuses [name]   registrations, formed-at, likes/dislikes and bans; with no name, game-wide likes/dislikes histograms
int StatusesCommand(string name)
{
    using var session = Attach(writable: false);
    if (name.Length == 0)
    {
        var values = new SortedDictionary<int, int>(); var reasons = new SortedDictionary<string, int>(); var flags = new SortedDictionary<string, int>();
        foreach (var r in session.Players.LoadAll().SelectMany(p => session.ReadStatuses(p).Relationships))
        {
            values[r.Value] = values.GetValueOrDefault(r.Value) + 1;
            reasons[$"{r.Kind}:{r.Reason}"] = reasons.GetValueOrDefault($"{r.Kind}:{r.Reason}") + 1;
            flags[$"{r.Kind}:{r.Flags:X2}"] = flags.GetValueOrDefault($"{r.Kind}:{r.Flags:X2}") + 1;
        }
        Console.WriteLine("values: " + string.Join(" ", values.Select(v => $"{v.Key}×{v.Value}")));
        Console.WriteLine("reasons: " + string.Join(" ", reasons.Select(v => $"{v.Key}×{v.Value}")));
        Console.WriteLine("flags: " + string.Join(" ", flags.Select(v => $"{v.Key}×{v.Value}")));
        return 0;
    }
    foreach (var p in session.Players.LoadAll().Where(p => p.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)))
    {
        var s = session.ReadStatuses(p);
        Console.WriteLine($"{p.DisplayName} ({p.Club?.DisplayName}):");
        foreach (var r in s.Registrations) Console.WriteLine($"  registered {r.Club?.DisplayName} kind {r.Kind}");
        foreach (var c in s.TrainedAtClubs) Console.WriteLine($"  trained at club {c.Club?.DisplayName} period {c.Period}");
        foreach (var n in s.TrainedAtNations) Console.WriteLine($"  trained at nation {n.Nation?.DisplayName} period {n.Period}");
        foreach (var r in s.Relationships) Console.WriteLine($"  {r.Kind} {r.Name} value {r.Value} reason {r.Reason} flags 0x{r.Flags:X2} record 0x{r.Address:X}");
        foreach (var b in session.ReadBans(p)) Console.WriteLine($"  ban scope {b.Scope} item {b.ItemId} from {b.Start:yyyy-MM-dd} matches {b.MatchesFulfilled}/{b.Matches} days {b.Days} reason {b.Reason}");
    }
    return 0;
}

// removebans <name>   remove match bans only; day and global bans stay (writes to the game)
int RemoveBansCommand(string name)
{
    using var session = Attach(writable: true);
    if (OnePlayer(session, name) is not { } p) return 2;
    void Show(string when) => Console.WriteLine($"{when}: " + string.Join("; ", session.ReadBans(p).Select(b => $"scope {b.Scope} matches {b.MatchesFulfilled}/{b.Matches} days {b.Days} reason {b.Reason}")));
    Show("before");
    Console.WriteLine($"removed {session.RemoveMatchBans(p)}");
    Show("after ");
    return 0;
}

// destroy <name>   morale 1, condition and sharpness 5000, jadedness 1000 (writes to the game)
int DestroyCommand(string name)
{
    using var session = Attach(writable: true);
    if (OnePlayer(session, name) is not { } p) return 2;
    ShowFitness(session, p, "before");
    session.Destroy(p);
    ShowFitness(session, p, "after ");
    return 0;
}

// setplayer <name> <PlayerField> <value>   guarded write of one mapped player field
int SetPlayerField(string name, string fieldName, long value)
{
    var field = Enum.Parse<Fm27Editor.Core.Players.PlayerField>(fieldName, ignoreCase: true);
    using var session = Attach(writable: true);
    if (OnePlayer(session, name) is not { } p) return 2;
    long before = session.PlayerFields.Read(p, field) ?? throw new InvalidOperationException($"{field} is not readable");
    session.PlayerFields.Set(p, field, before, value);
    Console.WriteLine($"{p.DisplayName} {field}: {before} -> {session.PlayerFields.Read(p, field)}");
    return 0;
}

static PlayerRecord? OnePlayer(GameSession session, string name)
{
    var matches = session.Players.LoadAll().Where(p => p.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
    if (matches.Count == 1) return matches[0];
    Console.Error.WriteLine($"{matches.Count} players named '{name}'");
    return null;
}

static void ShowFitness(GameSession session, PlayerRecord p, string when)
{
    var v = session.PlayerFields.ReadAll(p);
    var inj = session.ReadInjuries(p);
    Console.WriteLine($"{when}: injuries {inj.Count} morale {v.GetValueOrDefault(Fm27Editor.Core.Players.PlayerField.Morale)} condition {v.GetValueOrDefault(Fm27Editor.Core.Players.PlayerField.Condition)} sharpness {v.GetValueOrDefault(Fm27Editor.Core.Players.PlayerField.MatchSharpness)} jadedness {v.GetValueOrDefault(Fm27Editor.Core.Players.PlayerField.Jadedness)}");
}

// heal <name>   remove all injuries, condition and sharpness 10000, jadedness -500 (writes to the game)
int HealCommand(string name)
{
    using var session = Attach(writable: true);
    var matches = session.Players.LoadAll().Where(p => p.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
    if (matches.Count != 1) { Console.Error.WriteLine($"{matches.Count} players named '{name}'"); return 2; }
    var p = matches[0];
    void Show(string when)
    {
        var v = session.PlayerFields.ReadAll(p);
        var inj = session.ReadInjuries(p);
        Console.WriteLine($"{when}: injuries {inj.Count} ({string.Join(", ", inj.Select(i => i.Name))}) condition {v.GetValueOrDefault(Fm27Editor.Core.Players.PlayerField.Condition)} sharpness {v.GetValueOrDefault(Fm27Editor.Core.Players.PlayerField.MatchSharpness)} jadedness {v.GetValueOrDefault(Fm27Editor.Core.Players.PlayerField.Jadedness)}");
    }
    Show("before");
    session.Heal(p);
    Show("after ");
    return 0;
}

// preset heal|inspire|destroy|allinone|show <id>... [--check]   run one of the FMRTE presets and read every field back
int PresetRunCommand(string[] args)
{
    string kind = args.Length > 0 ? args[0].ToLowerInvariant() : "";
    if (kind is not ("heal" or "inspire" or "destroy" or "allinone" or "show"))
    {
        Console.Error.WriteLine("usage: preset heal|inspire|destroy|allinone|show <player id>... [--check]");
        return 2;
    }
    bool check = args.Contains("--check");
    int[] ids = args.Skip(1).Where(a => !a.StartsWith("--")).Select(int.Parse).ToArray();
    using var session = Attach(writable: kind != "show");
    var date = session.ReadGameDate() ?? throw new InvalidOperationException("Game date unavailable");
    var all = session.Players.LoadAll();
    var cohesion = new TeamCohesionWriter(session.Memory, session.Profile, session.ModuleBase);
    Console.WriteLine($"game date {date:yyyy-MM-dd}");

    (long? Morale, long? Condition, long? Sharpness, long? Jadedness, int Injuries, int? Happiness, int? PlayingTime,
        ulong Club, int? Cohesion, IReadOnlyList<PlayerUnhappiness> Motives) Snap(PlayerRecord p)
    {
        var v = session.PlayerFields.ReadAll(p);
        var c = session.Contracts.Read(p);
        ulong club = PlayingClub.Of(session.Memory, session.Profile, session.ModuleBase, p);
        return (v.GetValueOrDefault(PlayerField.Morale), v.GetValueOrDefault(PlayerField.Condition),
            v.GetValueOrDefault(PlayerField.MatchSharpness), v.GetValueOrDefault(PlayerField.Jadedness),
            session.ReadInjuries(p).Count, c?.Happiness, c?.PlayingTimeHappiness, club,
            club == 0 ? null : cohesion.Read(p, club), session.ReadUnhappiness(p));
    }

    void Show(string when, PlayerRecord p, (long? Morale, long? Condition, long? Sharpness, long? Jadedness, int Injuries,
        int? Happiness, int? PlayingTime, ulong Club, int? Cohesion, IReadOnlyList<PlayerUnhappiness> Motives) s)
    {
        Console.WriteLine($"{when} {p.Id} {p.DisplayName}: morale {s.Morale} condition {s.Condition} sharpness {s.Sharpness} jadedness {s.Jadedness} " +
            $"injuries {s.Injuries} happiness {s.Happiness} playingTime {s.PlayingTime} club 0x{s.Club:X} cohesion {s.Cohesion}");
        Console.WriteLine($"  {s.Motives.Count} motives: " + string.Join("; ", s.Motives.Select(m =>
            $"motive {m.Motive} type {m.Type} {m.Start:yyyy-MM-dd}..{m.End:yyyy-MM-dd} {(m.IsActive(date) ? "ACTIVE" : "ended")}")));
    }

    int failures = 0;
    void Check(string what, bool ok)
    {
        Console.WriteLine($"CHECK {what}: {(ok ? "PASS" : "FAIL")}");
        if (!ok) failures++;
    }

    foreach (int id in ids)
    {
        var p = all.SingleOrDefault(x => x.Id == id) ?? throw new InvalidOperationException($"player {id} not found");
        var before = Snap(p);
        Show("before", p, before);
        if (kind != "show")
        {
            switch (kind)
            {
                case "heal": session.Heal(p); break;
                case "inspire": session.Inspire(p); break;
                case "destroy": session.Destroy(p); break;
                case "allinone": session.AllInOne(p); break;
            }
        }
        var after = Snap(p);
        Show("after ", p, after);
        if (!check || kind == "show") continue;

        // The same start-date guard EndMotives uses: a motive that starts after the game date keeps its end.
        bool motivesOk = after.Motives.Count == before.Motives.Count
            && after.Motives.Zip(before.Motives).All(x => x.Second.Start is { } s && s > date
                ? x.First.End == x.Second.End
                : x.First.End == date);
        switch (kind)
        {
            case "heal":
                Check("jadedness 0", after.Jadedness == 0);
                Check("condition 10000", after.Condition == 10000);
                Check("sharpness 10000", after.Sharpness == 10000);
                Check("no injuries", after.Injuries == 0);
                break;
            case "inspire":
                Check("morale 20", after.Morale == 20);
                Check("jadedness 0", after.Jadedness == 0);
                Check("happiness 100", after.Happiness is null or 100);
                Check("playing time 100", after.PlayingTime is null or 100);
                Check("cohesion 100 when the game keeps one", after.Cohesion is null or 100);
                Check("motive count unchanged", after.Motives.Count == before.Motives.Count);
                Check("every started motive ends on the game date", motivesOk);
                break;
            case "destroy":
                Check("morale 0", after.Morale == 0);
                Check("jadedness 1000", after.Jadedness == 1000);
                Check("condition 5000", after.Condition == 5000);
                Check("sharpness 5000", after.Sharpness == 5000);
                Check("happiness 0", after.Happiness is null or 0);
                Check("playing time 0", after.PlayingTime is null or 0);
                Check("cohesion 1 when the game keeps one", after.Cohesion is null or 1);
                break;
            case "allinone":
                Check("condition 10000", after.Condition == 10000);
                Check("sharpness 10000", after.Sharpness == 10000);
                Check("jadedness 0", after.Jadedness == 0);
                Check("morale 20", after.Morale == 20);
                Check("happiness 100", after.Happiness is null or 100);
                Check("playing time 100", after.PlayingTime is null or 100);
                Check("cohesion 100 when the game keeps one", after.Cohesion is null or 100);
                Check("every started motive ends on the game date", motivesOk);
                Check("no match bans", session.ReadBans(p).Count(BanWriter.IsMatchBan) == 0);
                Check("tactical familiarity 100", session.PlayerFamiliarity.Read(p.Address)?.Tactical == 100);
                break;
        }
    }
    if (failures > 0) { Console.WriteLine($"{failures} check(s) failed"); return 3; }
    return 0;
}

// goldengen <player id>... [--min N --max N --seed N] [--write] [--undo]   FM Studio's Golden Generation (default PA 150-180)
int GoldenGenCommand(string[] rest)
{
    int min = GoldenGenerationWriter.DefaultMinPa, max = GoldenGenerationWriter.DefaultMaxPa;
    int? seed = null;
    bool write = false, undo = false;
    var ids = new List<int>();
    for (int i = 0; i < rest.Length; i++)
    {
        switch (rest[i])
        {
            case "--min": min = int.Parse(rest[++i]); break;
            case "--max": max = int.Parse(rest[++i]); break;
            case "--seed": seed = int.Parse(rest[++i]); break;
            case "--write": write = true; break;
            case "--undo": write = true; undo = true; break;
            default: ids.Add(int.Parse(rest[i])); break;
        }
    }
    if (ids.Count == 0) { Console.Error.WriteLine("usage: goldengen <player id>... [--min N --max N --seed N] [--write] [--undo]"); return 2; }
    using var session = Attach(writable: write);
    var all = session.Players.LoadAll();
    var writer = new GoldenGenerationWriter(session.Memory, session.Profile, session.ModuleBase,
        seed is { } s ? new Random(s) : null);
    Console.WriteLine($"min {min} max {max} seed {seed?.ToString() ?? "shared"}");
    string Values(PlayerRecord p) =>
        $"PA {p.PotentialAbility} CA {p.CurrentAbility} Professionalism {p.Personality[(int)PersonalityAttribute.Professionalism]} Ambition {p.Personality[(int)PersonalityAttribute.Ambition]}";
    foreach (int id in ids)
    {
        var p = all.SingleOrDefault(x => x.Id == id) ?? throw new InvalidOperationException($"player {id} not found");
        var plan = writer.Plan(p, min, max);
        Console.WriteLine($"{id} {p.DisplayName}: {Values(p)} -> PA {plan.After.PotentialAbility} CA {plan.After.CurrentAbility} " +
            $"Professionalism {plan.After.Professionalism} Ambition {plan.After.Ambition}");
        if (!write) continue;
        writer.Apply(plan);
        Console.WriteLine($"  written: {Values(session.Players.Reload(p.Address) ?? throw new StaleObjectException("player vanished"))}");
        if (!undo) continue;
        writer.Undo(plan);
        Console.WriteLine($"  undone:  {Values(session.Players.Reload(p.Address) ?? throw new StaleObjectException("player vanished"))}");
    }
    return 0;
}

// compdiag [n]   RE (read only): the competitions whose Name reads empty, with both string pointers and what they hold
int CompDiagCommand(string[] args)
{
    int limit = args.Length > 0 ? int.Parse(args[0]) : 20;
    using var session = Attach(writable: false);
    var c = session.Profile.Competition ?? throw new InvalidOperationException("Profile has no competition offsets");
    var mem = session.Memory;
    var all = session.Competitions.LoadAll();
    string Describe(ulong p)
    {
        if (p == 0) return "null";
        try
        {
            int len = BitConverter.ToInt32(mem.Read(p, 4));
            string text = len is > 0 and < 400 ? System.Text.Encoding.UTF8.GetString(mem.Read(p + 4, len)) : $"len {len}";
            return $"0x{p:X} '{text}'";
        }
        catch (Exception ex) { return $"0x{p:X} unreadable {ex.GetType().Name}"; }
    }
    int unnamed = 0, shortOnly = 0;
    foreach (var r in all)
    {
        if (r.Name is not null) continue;
        unnamed++;
        if (r.ShortName is not null) shortOnly++;
        if (unnamed > limit) continue;
        ulong np = mem.ReadUInt64(r.Address + (ulong)c.Name), sp = mem.ReadUInt64(r.Address + (ulong)c.ShortName);
        ulong vt = mem.ReadUInt64(r.Address);
        Console.WriteLine($"id {r.Id} index {r.Index} vtable 0x{vt:X}{(vt == session.ModuleBase + c.VtableRva ? "" : " (not competition vtable)")} " +
            $"name {Describe(np)} short {Describe(sp)} rep {r.Reputation}");
    }
    Console.WriteLine($"{unnamed} of {all.Count} have no Name; {shortOnly} of those have a ShortName");
    foreach (var r in all.Where(r => r.Name is not null).Take(3))
        Console.WriteLine($"named sample id {r.Id} index {r.Index} '{r.Name}' ptr {Describe(mem.ReadUInt64(r.Address + (ulong)c.Name))}");
    return 0;
}

// compobj <competition id>   RE (read only): hex of the 0x1B0 object and every qword in it that points at a length-prefixed string
int CompObjCommand(int id)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var record = session.Competitions.LoadAll().FirstOrDefault(r => r.Id == id) ?? throw new InvalidOperationException($"competition {id} not found");
    var b = mem.Read(record.Address, 0x1B0);
    Console.WriteLine($"id {id} index {record.Index} at 0x{record.Address:X} name '{record.Name}' short '{record.ShortName}'");
    for (int row = 0; row < b.Length; row += 16)
        Console.WriteLine($"  +0x{row:X3} {Convert.ToHexString(b, row, 16)}");
    for (int at = 0; at + 8 <= b.Length; at += 8)
    {
        ulong p = BitConverter.ToUInt64(b, at);
        if (p < 0x10000 || p > 0x7FFFFFFFFFFF) continue;
        try
        {
            int len = BitConverter.ToInt32(mem.Read(p, 4));
            if (len is > 0 and < 400)
            {
                string text = System.Text.Encoding.UTF8.GetString(mem.Read(p + 4, len));
                if (text.All(ch => ch >= ' ' && ch != '\u007f'))
                    Console.WriteLine($"  +0x{at:X3} 0x{p:X} -> '{text}'");
            }
        }
        catch (MemoryAccessException) { }
    }
    return 0;
}

// comproot [startRva endRva]   RE (read only): find a module global whose +listOffset vector holds competition pointers
int CompRootCommand(string[] args)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var c = session.Profile.Competition ?? throw new InvalidOperationException("Profile has no competition offsets");
    ulong mb = session.Game.ModuleBase, vt = mb + c.VtableRva;
    int off = c.ListOffset ?? throw new InvalidOperationException("Profile has no competition list offset");
    ulong start = mb + (args.Length > 0 ? Convert.ToUInt64(args[0], 16) : 0x5500000UL);
    ulong end = mb + (args.Length > 1 ? Convert.ToUInt64(args[1], 16) : 0x5800000UL);
    ulong modSize = (ulong)session.Game.ModuleSize;
    bool Heap(ulong p) => p is > 0x10000 and < 0x7FFFFFFFFFFF && !(p >= mb && p < mb + modSize);
    ulong Ptr(ulong at) { Span<byte> b = stackalloc byte[8]; return mem.TryRead(at, b) ? BitConverter.ToUInt64(b) : 0; }
    int found = 0;
    var chunk = new byte[8 << 20];
    for (ulong at = start; at < end; at += (ulong)chunk.Length)
    {
        int n = (int)Math.Min((ulong)chunk.Length, end - at);
        if (!mem.TryRead(at, chunk.AsSpan(0, n))) continue;
        for (int i = 0; i + 8 <= n; i += 8)
        {
            ulong root = BitConverter.ToUInt64(chunk, i);
            if (root == 0 || root % 8 != 0 || !Heap(root)) continue;
            ulong header = root + (ulong)off;
            var h = new byte[24];
            if (!mem.TryRead(header, h)) continue;
            ulong begin = BitConverter.ToUInt64(h), stop = BitConverter.ToUInt64(h, 8), cap = BitConverter.ToUInt64(h, 16);
            if (begin == 0 || stop <= begin || cap < stop || (stop - begin) % 8 != 0) continue;
            ulong count = (stop - begin) / 8;
            if (count is < 1000 or > 100000) continue;
            bool ok = true;
            for (int k = 0; k < 3; k++)
            {
                ulong p = Ptr(begin + (ulong)(k * 8));
                if (p == 0 || Ptr(p) != vt) { ok = false; break; }
            }
            if (!ok) continue;
            Console.WriteLine($"root 0x{root:X} (global module+0x{at - mb + (ulong)i:X}) count {count} begin 0x{begin:X}");
            var repo = session.Competitions;
            int named = 0;
            for (ulong p = begin, k = 0; p < stop && k < 5000; p += 8, k++) if (repo.Reload(Ptr(p)) is { Name: not null }) named++;
            Console.WriteLine($"  first 5000 entries: {named} named");
            if (++found >= 8) return 0;
        }
    }
    Console.WriteLine($"{found} candidate roots in module+0x{start - mb:X}..0x{end - mb:X}");
    return 0;
}

// comproot3 [startRva endRva]   RE (read only): for each heap object a module global points at, look for any offset inside it
// that holds a vector of competition pointers (begin/end/capacity). Prints global RVA, object, offset and entry count.
int CompRoot3Command(string[] args)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var c = session.Profile.Competition ?? throw new InvalidOperationException("Profile has no competition offsets");
    ulong mb = session.Game.ModuleBase, vt = mb + c.VtableRva, modSize = (ulong)session.Game.ModuleSize;
    ulong start = mb + (args.Length > 0 ? Convert.ToUInt64(args[0], 16) : 0x4C00000UL);
    ulong end = mb + (args.Length > 1 ? Convert.ToUInt64(args[1], 16) : 0x6000000UL);
    bool Heap(ulong p) => p is > 0x10000 and < 0x7FFFFFFFFFFF && !(p >= mb && p < mb + modSize);
    ulong Ptr(ulong at) { Span<byte> b = stackalloc byte[8]; return mem.TryRead(at, b) ? BitConverter.ToUInt64(b) : 0; }
    int shown = 0, tested = 0;
    var chunk = new byte[8 << 20];
    for (ulong at = start; at < end; at += (ulong)chunk.Length)
    {
        int n = (int)Math.Min((ulong)chunk.Length, end - at);
        if (!mem.TryRead(at, chunk.AsSpan(0, n))) continue;
        for (int i = 0; i + 8 <= n; i += 8)
        {
            ulong obj = BitConverter.ToUInt64(chunk, i);
            if (obj == 0 || obj % 8 != 0 || !Heap(obj)) continue;
            var window = new byte[0x100];
            if (!mem.TryRead(obj, window)) continue;
            tested++;
            for (int off = 0; off + 24 <= window.Length; off += 8)
            {
                ulong begin = BitConverter.ToUInt64(window, off), stop = BitConverter.ToUInt64(window, off + 8), cap = BitConverter.ToUInt64(window, off + 16);
                if (begin == 0 || stop <= begin || cap < stop || (stop - begin) % 8 != 0) continue;
                ulong count = (stop - begin) / 8;
                if (count is < 50 or > 200000) continue;
                bool ok = true;
                for (int k = 0; k < 3; k++)
                {
                    ulong p = Ptr(begin + (ulong)(k * 8));
                    if (p == 0 || Ptr(p) != vt) { ok = false; break; }
                }
                if (!ok) continue;
                Console.WriteLine($"global module+0x{at - mb + (ulong)i:X} -> object 0x{obj:X}, vector at object+0x{off:X}: begin 0x{begin:X} count {count}");
                var repo = session.Competitions;
                int named = 0, probe = (int)Math.Min(count, 5000UL);
                for (int k = 0; k < probe; k++) if (repo.Reload(Ptr(begin + (ulong)(k * 8))) is { Name: not null }) named++;
                Console.WriteLine($"  first {probe}: {named} named");
                if (++shown >= 6) { Console.WriteLine($"{tested} objects tested"); return 0; }
            }
        }
    }
    Console.WriteLine($"{shown} vectors, {tested} objects tested in module+0x{start - mb:X}..0x{end - mb:X}");
    return 0;
}

// compbase <objectHex> <index> [startRva endRva]   RE (read only): the competition block base from one object's index, the
// vector header that spans it (base..base+count*0x1B0), and every module global that holds the base
int CompBaseCommand(string[] args)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var c = session.Profile.Competition ?? throw new InvalidOperationException("Profile has no competition offsets");
    ulong mb = session.Game.ModuleBase, vt = mb + c.VtableRva;
    ulong obj = Convert.ToUInt64(args[0].Replace("0x", ""), 16);
    int index = int.Parse(args[1]);
    ulong stride = CompetitionRepository.ObjectStride;
    ulong baseAddr = obj - (ulong)index * stride;
    Console.WriteLine($"object 0x{obj:X} index {index} -> block base 0x{baseAddr:X} (stride 0x{stride:X})");
    for (int k = 0; k < 3; k++)
    {
        ulong p = baseAddr + (ulong)k * stride;
        Console.WriteLine($"  entry {k} 0x{p:X} vtable 0x{mem.ReadUInt64(p):X}{(mem.ReadUInt64(p) == vt ? " competition" : "")}");
    }
    // The header that spans the block: a vector whose begin is the base and whose end is the last entry + 1.
    for (ulong back = 0; back <= 0x400; back += 8)
    {
        ulong h = baseAddr - back;
        var b = new byte[24];
        if (!mem.TryRead(h, b)) continue;
        ulong b0 = BitConverter.ToUInt64(b), b1 = BitConverter.ToUInt64(b, 8), b2 = BitConverter.ToUInt64(b, 16);
        if (b0 != baseAddr || b1 <= b0 || (b1 - b0) % stride != 0 || b2 < b1) continue;
        Console.WriteLine($"  vector header at 0x{h:X}: begin 0x{b0:X} end 0x{b1:X} cap 0x{b2:X} count {(b1 - b0) / stride}");
        break;
    }
    ulong start = mb + (args.Length > 2 ? Convert.ToUInt64(args[2], 16) : 0x4C00000UL);
    ulong end = mb + (args.Length > 3 ? Convert.ToUInt64(args[3], 16) : 0x6000000UL);
    var chunk = new byte[8 << 20];
    int hits = 0;
    for (ulong at = start; at < end; at += (ulong)chunk.Length)
    {
        int n = (int)Math.Min((ulong)chunk.Length, end - at);
        if (!mem.TryRead(at, chunk.AsSpan(0, n))) continue;
        for (int i = 0; i + 8 <= n; i += 8)
        {
            if (BitConverter.ToUInt64(chunk, i) != baseAddr) continue;
            Console.WriteLine($"  global module+0x{at - mb + (ulong)i:X} holds the block base");
            var around = mem.Read(at + (ulong)i - 0x20, 0x60);
            Console.WriteLine("    around: " + Convert.ToHexString(around));
            if (++hits >= 6) return 0;
        }
    }
    Console.WriteLine($"{hits} globals hold the block base");
    return 0;
}

// compref   RE (read only): what the profile's list slot points at, and whether any team's competition is one of the
// last objects (the ones with no name)
int CompRefCommand()
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var c = session.Profile.Competition ?? throw new InvalidOperationException("Profile has no competition offsets");
    ulong mb = session.Game.ModuleBase;
    ulong slot = c.ListRootRva is { } rva ? mem.TryRead(mb + (ulong)rva, new byte[8]) ? BitConverter.ToUInt64(mem.Read(mb + (ulong)rva, 8)) : 0 : 0;
    Console.WriteLine($"listRootRva 0x{c.ListRootRva:X} -> 0x{slot:X}");
    if (slot != 0 && c.ListOffset is { } off)
    {
        var h = mem.Read(slot + (ulong)off, 24);
        ulong begin = BitConverter.ToUInt64(h), end = BitConverter.ToUInt64(h, 8), cap = BitConverter.ToUInt64(h, 16);
        Console.WriteLine($"  +0x{off:X} vector begin 0x{begin:X} end 0x{end:X} cap 0x{cap:X} count {(end >= begin ? (end - begin) / 8 : 0)}");
        if (begin != 0) for (int k = 0; k < 3 && begin + (ulong)(k * 8) < end; k++)
        {
            ulong p = BitConverter.ToUInt64(mem.Read(begin + (ulong)(k * 8), 8));
            Console.WriteLine($"  entry {k} 0x{p:X} vtable 0x{(p != 0 ? mem.ReadUInt64(p) : 0):X} (competition vtable 0x{mb + c.VtableRva:X})");
        }
    }
    var all = session.Competitions.LoadAll();
    int count = all.Count;
    ulong strideV = CompetitionRepository.ObjectStride;
    var byIndex = all.OrderBy(r => r.Index).First();
    ulong blockBase = byIndex.Address - (ulong)byIndex.Index * strideV;
    Console.WriteLine($"competitions {count}, block base 0x{blockBase:X} stride 0x{strideV:X}, last object 0x{blockBase + (ulong)(count - 1) * strideV:X}");
    var perIndex = new Dictionary<int, int>();
    int teams = 0, inside = 0, unnamedUsed = 0;
    foreach (var club in session.LoadClubs(session.Players.LoadAll()))
        foreach (var team in club.Teams)
        {
            teams++;
            ulong p = mem.ReadUInt64(team.Address + (ulong)c.TeamCompetition);
            if (p < blockBase || p >= blockBase + (ulong)count * strideV || (p - blockBase) % strideV != 0) continue;
            inside++;
            int idx = (int)((p - blockBase) / strideV);
            perIndex[idx] = perIndex.GetValueOrDefault(idx) + 1;
            if (idx >= 8084) unnamedUsed++;
        }
    Console.WriteLine($"teams {teams}, with a competition in the block {inside}, pointing at an unnamed (index >= 8084) {unnamedUsed}");
    foreach (var (idx, n) in perIndex.Where(kv => kv.Key >= 8084).Take(10))
        Console.WriteLine($"  unnamed index {idx}: {n} teams");
    return 0;
}

// compfind <valueHex>   RE (read only): every writable location (module and heap) holding that qword, with 0x30 bytes around
int CompFindCommand(string[] args)
{
    ulong value = Convert.ToUInt64(args[0].Replace("0x", ""), 16);
    using var session = Attach(writable: false);
    var mem = session.Memory;
    Console.WriteLine($"looking for 0x{value:X}");
    int hits = 0;
    var chunk = new byte[4 << 20];
    void Scan(ulong start, ulong size)
    {
        for (ulong offset = 0; offset + 8 <= size; offset += (ulong)chunk.Length)
        {
            int length = (int)Math.Min((ulong)chunk.Length, size - offset);
            if (!mem.TryRead(start + offset, chunk.AsSpan(0, length))) return;
            for (int i = 0; i + 8 <= length; i += 8)
            {
                if (BitConverter.ToUInt64(chunk, i) != value) continue;
                ulong at = start + offset + (ulong)i;
                Console.WriteLine($"  at 0x{at:X}" + (at >= session.Game.ModuleBase && at < session.Game.ModuleEnd ? $" (module+0x{at - session.Game.ModuleBase:X})" : ""));
                try { Console.WriteLine("    around: " + Convert.ToHexString(mem.Read(at - 0x10, 0x40))); } catch (MemoryAccessException) { }
                if (++hits >= 8) return;
            }
        }
    }
    foreach (var region in session.Process.EnumerateWritableRegions(session.Game.ModuleBase, session.Game.ModuleEnd))
    {
        if (hits >= 8) break;
        Scan(region.Start, region.Size);
    }
    Console.WriteLine($"{hits} hits");
    return 0;
}

// hexdump <addressHex> [length]   RE (read only): qwords with 16-byte hex rows
int HexDumpCommand(string[] args)
{
    using var session = Attach(writable: false);
    ulong at = Convert.ToUInt64(args[0].Replace("0x", ""), 16);
    int len = args.Length > 1 ? Convert.ToInt32(args[1], 16) : 0x100;
    var b = session.Memory.Read(at, len);
    for (int row = 0; row < b.Length; row += 16)
    {
        string hex = Convert.ToHexString(b, row, Math.Min(16, b.Length - row));
        string q = row + 8 <= b.Length ? $"0x{BitConverter.ToUInt64(b, row):X}" : "";
        string q2 = row + 16 <= b.Length ? $"0x{BitConverter.ToUInt64(b, row + 8):X}" : "";
        Console.WriteLine($"+0x{row:X4} {hex,-32} {q,18} {q2,18}");
    }
    return 0;
}

// register <club id> <uefa|domestic> [--write]   squad-wide competition registration (FM Studio's RegisterSquadForUefa/Domestic)
int RegisterCommand(string[] args)
{
    if (args.Length < 2) { Console.Error.WriteLine("usage: register <club id or name> <uefa|domestic> [--write]"); return 2; }
    RegistrationKind kind = args[1].ToLowerInvariant() switch
    {
        "uefa" => RegistrationKind.Uefa,
        "domestic" => RegistrationKind.Domestic,
        _ => throw new ArgumentException("kind must be uefa or domestic")
    };
    bool write = args.Contains("--write");
    using var session = Attach(writable: write);
    var clubs = session.LoadClubs(session.Players.LoadAll());
    var club = (int.TryParse(args[0], out int id)
            ? clubs.FirstOrDefault(c => c.Id == id)
            : clubs.FirstOrDefault(c => c.DisplayName.Contains(args[0], StringComparison.OrdinalIgnoreCase)))
        ?? throw new InvalidOperationException($"club {args[0]} not found");
    ulong team = club.FirstTeam?.Address ?? throw new InvalidOperationException($"{club.DisplayName} has no first team");
    var squad = session.ReadSquad(team);
    var writer = new RelationshipWriter(session.Memory, session.Profile, session.ModuleBase);
    var missing = squad.Where(p => !writer.HasRegistration(session.ReadStatuses(p), team, (byte)kind)).ToList();
    Console.WriteLine($"{club.DisplayName} first team 0x{team:X}: {squad.Count} players, {missing.Count} without {kind} registration");
    foreach (var p in missing.Take(12)) Console.WriteLine($"  {p.Id} {p.DisplayName}");
    if (!write)
    {
        Console.WriteLine("dry run; add --write to register them");
        return 0;
    }
    int added = session.RegisterTeam(team, kind, squad);
    var after = session.ReadSquad(team);
    int still = after.Count(p => !writer.HasRegistration(session.ReadStatuses(p), team, (byte)kind));
    Console.WriteLine($"added {added} row(s); {after.Count - still}/{after.Count} players registered");
    return still == 0 ? 0 : 3;
}

// youth <club id or name> [--write]   the four youth-infrastructure values; --write maximises them (FM Studio's Maximize)
int YouthCommand(string[] args)
{
    if (args.Length < 1) { Console.Error.WriteLine("usage: youth <club id or name> [--write]"); return 2; }
    bool write = args.Contains("--write");
    using var session = Attach(writable: write);
    var clubs = session.LoadClubs(session.Players.LoadAll());
    var club = (int.TryParse(args[0], out int id)
            ? clubs.FirstOrDefault(c => c.Id == id)
            : clubs.FirstOrDefault(c => c.DisplayName.Contains(args[0], StringComparison.OrdinalIgnoreCase)))
        ?? throw new InvalidOperationException($"club {args[0]} not found");
    var academy = new YouthAcademy(session.Memory, session.Profile, session.ModuleBase);
    string Describe(YouthInfrastructure? y) => y is null
        ? "youth values unavailable"
        : $"facilities {y.Facilities}, coaching {y.Coaching}, recruitment {y.Recruitment}, importance {y.Importance}";
    Console.WriteLine($"{club.DisplayName} (0x{club.Address:X}): {Describe(academy.Read(club.Address))}");
    if (!write)
    {
        Console.WriteLine("dry run; add --write to set all four to 20");
        return 0;
    }
    bool changed = academy.Maximize(club.Address);
    Console.WriteLine($"{(changed ? "maximised" : "already maximal")}: {Describe(academy.Read(club.Address))}");
    return 0;
}

// squad <id>   first-team squad with player fields (condition, sharpness, morale, asking price...)
int InjuriesCommand(string query)
{
    using var session = Attach(writable: false);
    foreach (var p in session.Players.LoadAll().Where(p => query.Length == 0 || p.Id.ToString() == query || p.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)))
        foreach (var i in session.ReadInjuries(p))
            Console.WriteLine($"{p.Id} | {p.DisplayName} | player 0x{p.Address:X} | injury 0x{i.Address:X} | {i.TypeId} {i.Name} | {i.Start:yyyy-MM-dd} | {i.MinimumDays}+{i.ExtraDays} | tail {i.Snapshot[0x58..]}");
    return 0;
}

int BansCommand(string query)
{
    using var session = Attach(writable: false);
    foreach (var p in session.Players.LoadAll().Where(p => query.Length == 0 || p.Id.ToString() == query || p.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)))
        foreach (var b in session.ReadBans(p))
            Console.WriteLine($"{p.Id} | {p.DisplayName} | player 0x{p.Address:X} | ban 0x{b.Address:X} | scope {b.Scope} item {b.ItemId} | {b.Start:yyyy-MM-dd} | days {b.Days} matches {b.MatchesFulfilled}/{b.Matches} points {b.Points} reason {b.Reason}");
    return 0;
}

// personidx <object>   index of the person-list entry pointing within ±0x400 of an object (read only)
int PersonIndexCommand(ulong address)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    ulong obj = mem.ReadUInt64(session.Game.ModuleBase + 0x5703738);
    ulong begin = mem.ReadUInt64(obj + 0x88), end = mem.ReadUInt64(obj + 0x90);
    var ptrs = mem.Read(begin, (int)(end - begin));
    for (int i = 0; i < ptrs.Length; i += 8)
    {
        ulong p = BitConverter.ToUInt64(ptrs, i);
        if (p + 0x400 >= address && p <= address + 0x400) Console.WriteLine($"index {i / 8} -> 0x{p:X} (object {(long)(p - address):+#;-#;0})");
    }
    return 0;
}

// near32 <value> <hex>...   every 4-aligned heap int32 equal to value with one of the byte patterns within ±0x20 (read only)
int Near32Command(int value, string[] patterns)
{
    using var session = Attach(writable: false);
    var needles = patterns.Select(Convert.FromHexString).ToArray();
    var chunk = new byte[1 << 20]; int hits = 0, refs = 0;
    foreach (var region in session.Process.EnumerateWritableRegions(session.Game.ModuleBase, session.Game.ModuleEnd).Where(r => !r.IsModuleData))
        for (ulong offset = 0; offset < region.Size; offset += (ulong)chunk.Length - 0x40)
        {
            int length = (int)Math.Min((ulong)chunk.Length, region.Size - offset);
            if (!session.Memory.TryRead(region.Start + offset, chunk.AsSpan(0, length))) continue;
            for (int k = 0x20; k + 0x24 <= length; k += 4)
            {
                if (BitConverter.ToInt32(chunk, k) != value) continue;
                refs++;
                var window = chunk.AsSpan(k - 0x20, 0x44).ToArray();
                if (needles.Length > 0 && !needles.Any(n => window.AsSpan().IndexOf(n) >= 0)) continue;
                if (++hits <= 200) Console.WriteLine($"0x{region.Start + offset + (ulong)k:X}  {Convert.ToHexString(window, 0, 0x20)} [{Convert.ToHexString(window, 0x20, 4)}] {Convert.ToHexString(window, 0x24, 0x20)}");
            }
        }
    Console.WriteLine($"{refs} refs, {hits} with pattern");
    return 0;
}

// neardate <day> <year> <hex>...   every 2-aligned game date (low 9 bits = day, any flag bits) with a byte pattern within ±0x40 (read only)
int NearDateCommand(int day, int year, string[] patterns)
{
    using var session = Attach(writable: false);
    var needles = patterns.Select(Convert.FromHexString).ToArray();
    var chunk = new byte[1 << 20]; int hits = 0, refs = 0;
    foreach (var region in session.Process.EnumerateWritableRegions(session.Game.ModuleBase, session.Game.ModuleEnd).Where(r => !r.IsModuleData))
        for (ulong offset = 0; offset < region.Size; offset += (ulong)chunk.Length - 0x80)
        {
            int length = (int)Math.Min((ulong)chunk.Length, region.Size - offset);
            if (!session.Memory.TryRead(region.Start + offset, chunk.AsSpan(0, length))) continue;
            for (int k = 0x40; k + 0x44 <= length; k += 2)
            {
                if (BitConverter.ToInt16(chunk, k + 2) != year || (BitConverter.ToUInt16(chunk, k) & 0x1FF) != day || chunk[k + 1] == 0) continue;
                refs++;
                var window = chunk.AsSpan(k - 0x40, 0x84).ToArray();
                if (needles.Length > 0 && !needles.Any(n => window.AsSpan().IndexOf(n) >= 0)) continue;
                if (++hits <= 200) Console.WriteLine($"0x{region.Start + offset + (ulong)k:X}  {Convert.ToHexString(window, 0, 0x40)} [{Convert.ToHexString(window, 0x40, 4)}] {Convert.ToHexString(window, 0x44, 0x40)}");
            }
        }
    Console.WriteLine($"{refs} flagged dates, {hits} with pattern");
    return 0;
}

// languages [id or name]   every language record; with no query, also the value and record-byte histograms (read only)
int LanguagesCommand(string query)
{
    using var session = Attach(writable: false);
    var values = new SortedDictionary<int, int>(); var tails = new Dictionary<string, int>(); var counts = new SortedDictionary<int, int>();
    foreach (var p in session.Players.LoadAll().Where(p => query.Length == 0 || p.Id.ToString() == query || p.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)))
    {
        var languages = session.ReadLanguages(p);
        counts[languages.Count] = counts.GetValueOrDefault(languages.Count) + 1;
        foreach (var l in languages)
        {
            values[l.Value] = values.GetValueOrDefault(l.Value) + 1;
            tails[l.Snapshot[0x12..]] = tails.GetValueOrDefault(l.Snapshot[0x12..]) + 1;
            if (query.Length > 0) Console.WriteLine($"{p.Id} | {p.DisplayName} | record 0x{l.Address:X} | {l.LanguageId} {l.Name} | {l.Value}");
        }
    }
    if (query.Length == 0)
    {
        Console.WriteLine("languages per player: " + string.Join(" ", counts.Select(c => $"{c.Key}×{c.Value}")));
        Console.WriteLine("values: " + string.Join(" ", values.Select(c => $"{c.Key}×{c.Value}")));
        Console.WriteLine("bytes +0x9..+0xF: " + string.Join(" ", tails.OrderByDescending(t => t.Value).Take(8).Select(t => $"{t.Key}×{t.Value}")));
    }
    return 0;
}

int LanguageValueCommand(int playerId, ulong record, int value)
{
    using var session = Attach(writable: true);
    var player = session.Players.LoadAll().Single(p => p.Id == playerId);
    var language = session.ReadLanguages(player).Single(l => l.Address == record);
    new LanguageWriter(session.Memory, session.Profile, session.Game.ModuleBase).SetValue(player, language, value);
    Console.WriteLine($"{player.Id}: {language.Name} {language.Value} -> {value}");
    return 0;
}

// vecscan <object> <size>   every begin/end pointer pair in the object (and one hop through a holder): element count and the first bytes of up to 4 pointed-to elements (read only)
int VecScanCommand(ulong address, int size)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var q = new byte[16];
    void Scan(ulong at, int length, string prefix, bool recurse)
    {
        var o = new byte[length];
        if (!mem.TryRead(at, o)) return;
        for (int k = 0; k + 16 <= length; k += 8)
        {
            ulong begin = BitConverter.ToUInt64(o, k), end = BitConverter.ToUInt64(o, k + 8);
            if (begin == 0 || end <= begin || end - begin > 0x800 || begin >> 40 == 0 || !mem.TryRead(begin, q.AsSpan(0, 8))) { if (recurse && begin >> 40 != 0 && begin != 0) Scan(begin, 0x40, $"{prefix}+0x{k:X}->", false); continue; }
            var items = mem.Read(begin, (int)Math.Min(end - begin, 0x40));
            var preview = new List<string>();
            for (int i = 0; i + 8 <= items.Length && preview.Count < 4; i += 8)
            {
                ulong p = BitConverter.ToUInt64(items, i);
                preview.Add(p >> 40 != 0 && mem.TryRead(p, q) ? Convert.ToHexString(q) : $"inline {Convert.ToHexString(items, i, 8)}");
            }
            Console.WriteLine($"{prefix}+0x{k:X}: {(end - begin) / 8} slots  {string.Join(" | ", preview)}");
        }
    }
    Scan(address, size, "", true);
    return 0;
}

// reltype <hex type>   every player relation record with that type byte, raw (read only)
// cities <name>   cities whose name contains the text, with id and nation pointer (read only)
int CitiesCommand(string query)
{
    using var session = Attach(writable: false);
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var all = session.Cities.LoadAll();
    Console.WriteLine($"{all.Count} cities in {sw.ElapsedMilliseconds} ms");
    foreach (var c in all.Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(20)) Console.WriteLine($"{c.Id,10} {c.Name} 0x{c.Address:X} nation 0x{c.Nation:X}");
    return 0;
}

// playersborn <city id>   exact city ID, not a city-name substring (read only)
int PlayersBornCommand(int cityId)
{
    using var session = Attach(writable: false);
    var players = session.Players.LoadAll();
    var matches = session.Cities.FindPlayersBornInCity(players, cityId);
    foreach (var p in matches) Console.WriteLine($"{p.Id,10} {p.DisplayName} · {p.ClubName}");
    Console.WriteLine($"{matches.Count} players born in city {cityId}");
    return 0;
}

// playerscontract <type|any> <playing-time|any>  (read only; enum names as in Core)
int PlayersContractCommand(string typeText, string playingText, string[] ranges)
{
    ContractType? type = typeText.Equals("any", StringComparison.OrdinalIgnoreCase) ? null : Enum.Parse<ContractType>(typeText, true);
    PlayingTime? playing = playingText.Equals("any", StringComparison.OrdinalIgnoreCase) ? null : Enum.Parse<PlayingTime>(playingText, true);
    using var session = Attach(writable: false);
    if (ranges.Length is not (0 or 4)) throw new ArgumentException("Use: playerscontract <type|any> <playing|any> [min-wage max-wage min-price max-price]; use any for an open bound");
    long? Bound(int index) => ranges.Length == 0 || ranges[index].Equals("any", StringComparison.OrdinalIgnoreCase) ? null : long.Parse(ranges[index]);
    var result = new ContractSearch(session.Contracts).Find(session.Players.LoadAll(), new(type, playing, Bound(0), Bound(1), Bound(2), Bound(3)));
    foreach (var p in result.Matches.Take(30)) Console.WriteLine($"{p.Id,10} {p.DisplayName} · {p.ClubName}");
    Console.WriteLine($"{result.Matches.Count} matches; {result.Unavailable.Count} unavailable");
    return 0;
}

// playerscontinent <exact continent name> — primary nationality only (read only)
int PlayersClauseCommand(string typeText, string presence, string[] bounds)
{
    bool present = presence.Equals("present", StringComparison.OrdinalIgnoreCase) ? true : presence.Equals("absent", StringComparison.OrdinalIgnoreCase) ? false : throw new ArgumentException("Use present or absent");
    if (bounds.Length is not (0 or 2)) throw new ArgumentException("Use: playersclause <raw-type> <present|absent> [min max]");
    int? Bound(int i) => bounds.Length == 0 || bounds[i].Equals("any", StringComparison.OrdinalIgnoreCase) ? null : int.Parse(bounds[i]);
    using var session = Attach(writable: false);
    var result = new ContractSearch(session.Contracts).Find(session.Players.LoadAll(), new(ClauseType: short.Parse(typeText), HasClause: present, MinimumClauseValue: Bound(0), MaximumClauseValue: Bound(1)));
    foreach (var p in result.Matches.Take(30)) Console.WriteLine($"{p.Id,10} {p.DisplayName} · {p.ClubName}");
    Console.WriteLine($"{result.Matches.Count} matches; {result.Unavailable.Count} unavailable");
    return 0;
}

// playerscontinent <exact continent name> — primary nationality only (read only)
int PlayersContinentCommand(string name)
{
    using var session = Attach(writable: false);
    var continent = session.NationLinks.Continents(session.Nations.LoadAll()).SingleOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    if (continent is null) { Console.Error.WriteLine("Continent not found"); return 2; }
    var result = new PlayerContinentSearch(session.NationLinks).Find(session.Players.LoadAll(), continent);
    foreach (var p in result.Matches.Take(30)) Console.WriteLine($"{p.Id,10} {p.DisplayName} · {p.NationalityName}");
    Console.WriteLine($"{result.Matches.Count} matches; {result.Unavailable.Count} unavailable");
    return 0;
}

// playersnation <nation-id> [--include-other] — exact nation object (read only)
int PlayersTrainedCommand(string targetKind, int id)
{
    using var session = Attach(writable: false);
    var players = session.Players.LoadAll();
    PlayerRelationCriteria criteria;
    if (targetKind.Equals("club", StringComparison.OrdinalIgnoreCase))
    {
        var club = session.LoadClubs(players).SingleOrDefault(c => c.Id == id) ?? throw new ArgumentException("Club ID not found in loaded players");
        criteria = new(TrainedAtClub: club.Address);
    }
    else if (targetKind.Equals("nation", StringComparison.OrdinalIgnoreCase))
    {
        var nation = session.Nations.LoadAll().SingleOrDefault(n => n.Id == id) ?? throw new ArgumentException("Nation ID not found");
        criteria = new(TrainedAtNation: nation.Address);
    }
    else throw new ArgumentException("Use playerstrained <club|nation> <id>");
    var reader = new PlayerStatusReader(session.Memory, session.Profile, session.ModuleBase);
    var result = new PlayerRelationSearch(reader).Find(players, criteria);
    foreach (var player in result.Matches.Take(30)) Console.WriteLine($"{player.Id,10} {player.DisplayName} · {player.ClubName}");
    Console.WriteLine($"{result.Matches.Count} matches; {result.Unavailable.Count} unavailable");
    return 0;
}

// playersnation <nation-id> [--include-other] — exact nation object (read only)
int PlayersWonderkidsCommand()
{
    using var session = Attach(writable: false);
    var date = session.ReadGameDate() ?? throw new InvalidOperationException("The in-game date could not be read");
    var players = session.Players.LoadAll();
    foreach (var player in players) player.GameDate = date;
    var matches = players.Where(p => WonderkidSearch.IsWonderkid(p) == true).ToArray();
    foreach (var player in matches.Take(30)) Console.WriteLine($"{player.Id,10} {player.DisplayName} · age {player.Age} · PA {player.PotentialAbility}");
    Console.WriteLine($"{matches.Length} matches; {players.Count(p => WonderkidSearch.IsWonderkid(p) is null)} unavailable");
    return 0;
}

// playersnation <nation-id> [--include-other] — exact nation object (read only)
int PlayersNationCommand(int nationId, bool includeOther)
{
    using var session = Attach(writable: false);
    var nation = session.Nations.LoadAll().SingleOrDefault(n => n.Id == nationId);
    if (nation is null) { Console.Error.WriteLine("Nation ID not found"); return 2; }
    var reader = new PlayerStatusReader(session.Memory, session.Profile, session.ModuleBase);
    var result = new PlayerNationalitySearch(reader).Find(session.Players.LoadAll(), nation, includeOther);
    foreach (var p in result.Matches.Take(30)) Console.WriteLine($"{p.Id,10} {p.DisplayName} · {p.NationalityName}");
    Console.WriteLine($"{result.Matches.Count} matches; {result.Unavailable.Count} unavailable");
    return 0;
}

// staffcontract <name>   staff job record [person+0xA8]: vtable and the contract fields the offered-contract reader sees (read only)
int StaffContractCommand(string query)
{
    using var session = Attach(writable: false);
    ulong mb = session.Game.ModuleBase;
    foreach (var s in session.Staff.LoadAll().Where(s => s.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(5))
    {
        ulong job = session.Memory.ReadUInt64(s.Address + 0xA8);
        ulong vt = job == 0 ? 0 : session.Memory.ReadUInt64(job) - mb;
        var c = job == 0 ? null : session.FutureTransfers.ReadContract(job, 0);
        Console.WriteLine($"{s.DisplayName} person 0x{s.Address:X} job 0x{job:X} vt +0x{vt:X}: " + (c is null ? "not a contract" : $"wage {c.Wage} type {c.Type} rolling {c.Rolling} {c.Started} - {c.Expires}"));
    }
    return 0;
}

// callups <name>   national squad of matching players, and how many players are called up overall (read only)
int CallUpsCommand(string query)
{
    using var session = Attach(writable: false);
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var all = session.CallUps.All();
    Console.WriteLine($"{all.Count} called-up players, {all.Values.Select(c => c.Team).Distinct().Count()} squads in {sw.ElapsedMilliseconds} ms");
    if (query is "--pair" or "-p")
    {
        // first squad with two called-up players: a swap test needs two team-mates
        var squad = session.Players.LoadAll().Where(p => all.ContainsKey(p.Address))
            .GroupBy(p => all[p.Address].Team).FirstOrDefault(g => g.Count() >= 2);
        if (squad is null) { Console.WriteLine("no squad with two called-up players"); return 2; }
        foreach (var p in squad.Take(2))
            Console.WriteLine($"{p.DisplayName} ({p.ClubName}) {session.CareerPlans.Item(all[p.Address].NationFootball)?.Name} team 0x{all[p.Address].Team:X} slot 0x{all[p.Address].Slot:X}");
        return 0;
    }
    if (query is "--list" or "-l")
    {
        // list called-up players grouped by squad so a swap can be tested on two team-mates
        foreach (var p in session.Players.LoadAll().Where(p => all.ContainsKey(p.Address)).Take(20))
            Console.WriteLine($"{p.DisplayName} ({p.ClubName}) {session.CareerPlans.Item(all[p.Address].NationFootball)?.Name} team 0x{all[p.Address].Team:X} slot 0x{all[p.Address].Slot:X}");
        return 0;
    }
    foreach (var p in session.Players.LoadAll().Where(p => p.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(10))
        Console.WriteLine(all.TryGetValue(p.Address, out var c) ? $"{p.DisplayName}: {session.CareerPlans.Item(c.NationFootball)?.Name} team 0x{c.Team:X} slot 0x{c.Slot:X}" : $"{p.DisplayName}: none");
    return 0;
}

// ctypes   histogram of contract +0x26 (int16) and +0xC3 (byte) over every player's current contract, with age samples (read only)
int ContractTypes()
{
    using var session = Attach(writable: false);
    var m = session.Memory; var date = session.ReadGameDate();
    var hist = new Dictionary<string, List<string>>();
    foreach (var p in session.Players.LoadAll())
    {
        ulong c = m.ReadUInt64(p.Address + 0x330);
        var b = new byte[0xC8];
        if (c == 0 || !m.TryRead(c, b)) continue;
        p.GameDate = date;
        string key = $"+0x26 {BitConverter.ToInt16(b, 0x26),3} +0xC3 {b[0xC3],3}";
        if (!hist.TryGetValue(key, out var l)) hist[key] = l = [];
        l.Add($"{p.DisplayName} {p.Id} age {p.Age}");
    }
    foreach (var (k, l) in hist.OrderByDescending(kv => kv.Value.Count).Take(25)) Console.WriteLine($"{l.Count,6} {k}  e.g. {string.Join("; ", l.Take(3))}");
    return 0;
}

// future-create <player id> <club id> <yyyy-MM-dd> <fee> [--write]
int FutureCreateCommand(string[] a)
{
    if (a.Length is < 4 or > 5 || !int.TryParse(a[0], out int id) || !int.TryParse(a[1], out int clubId) ||
        !DateOnly.TryParseExact(a[2], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var date) || !int.TryParse(a[3], out int fee) ||
        (a.Length == 5 && a[4] != "--write"))
    { Console.WriteLine("usage: future-create <player id> <club id> <yyyy-MM-dd> <fee> [--write]"); return 1; }
    bool write = a.Length == 5;
    using var session = Attach(writable: write);
    var all = session.Players.LoadAll();
    var player = all.Single(p => p.Id == id);
    var club = session.LoadClubs(all).Single(c => c.Id == clubId);
    ulong team = club.FirstTeam?.Address ?? throw new InvalidOperationException("Destination has no first team");
    var today = session.ReadGameDate() ?? throw new InvalidOperationException("Could not read game date");
    var creator = new FutureTransferCreator(session.Memory, session.Profile, session.ModuleBase);
    var plan = creator.Prepare(player, team, fee, today, date);
    Console.WriteLine($"{player.DisplayName} ({id}): {player.ClubName} -> {club.DisplayName} ({clubId}); fee {fee}; {date:yyyy-MM-dd}; offer ID {plan.Id}");
    if (!write) { Console.WriteLine("Preview only. Add --write to apply."); return 0; }
    if (session.ReadGameDate() != today) throw new StaleObjectException("Game date changed; nothing written");
    int offerId = creator.Apply(plan, size => RemoteCrtAllocator.Allocate(session.Process, size));
    var refreshed = session.Players.Reload(player.Address) ?? throw new MemoryAccessException("Cannot reload player after write");
    var offers = session.Proposals.Read(refreshed);
    var transfer = session.FutureTransfers.Read(refreshed, offers).SingleOrDefault(t => t.Offer.Id == offerId);
    if (transfer is null || transfer.Date != date || transfer.Offer.Value != fee)
        throw new MemoryAccessException("Write completed but future transfer did not read back as expected");
    Console.WriteLine($"Read back offer {offerId}: {transfer.Offer.From} -> {transfer.Offer.To}; fee {transfer.Offer.Value}; {transfer.Date:yyyy-MM-dd}");
    return 0;
}

// future <player id or name>   proposals with offered contracts and future transfer dates (read only)
int FutureCommand(string query)
{
    using var session = Attach(writable: false);
    bool byId = int.TryParse(query, out int playerId);
    foreach (var p in session.Players.LoadAll().Where(p => byId ? p.Id == playerId : p.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(5))
    {
        var proposals = session.Proposals.Read(p);
        var contracts = session.FutureTransfers.OfferContracts(p);
        Console.WriteLine($"{p.DisplayName} (ID {p.Id}): {proposals.Count} proposal(s), {contracts.Count} offered contract(s)");
        foreach (var x in proposals) Console.WriteLine($"  {x.Type} {x.From} -> {x.To} {x.Value} status {x.Status} id {x.Id}" + (contracts.TryGetValue(x.Id, out var c) ? $" | contract 0x{c.Address:X} wage {c.Wage} type {c.Type} rolling {c.Rolling} {c.Started} - {c.Expires} loan {c.Loan}" : ""));
        foreach (var f in session.FutureTransfers.Read(p, proposals)) Console.WriteLine($"  future transfer id {f.Offer.Id} on {f.Date}");
    }
    return 0;
}

// proposalsurvey   offers listed by each player (ids at [player+0x320]): one sample per vtable / status (read only)

// untrained <player>   removes the "trained at club" row for the player's current club, undoing that preset (write)
int UntrainedCommand(string[] words)
{
    string name = string.Join(" ", words);
    using var session = Attach(writable: true);
    var player = session.Players.LoadAll().FirstOrDefault(p => p.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"no player named '{name}'");
    ulong club = player.Club?.Address ?? 0;
    if (club == 0) { Console.WriteLine($"{player.DisplayName} has no club"); return 2; }
    bool removed = new RelationshipWriter(session.Memory, session.Profile, session.Game.ModuleBase).RemoveTrainedAtClub(player, club);
    Console.WriteLine(removed ? $"removed trained at club row for {player.DisplayName}" : $"no trained at club row for {player.DisplayName}");
    foreach (var c in session.ReadStatuses(player).TrainedAtClubs) Console.WriteLine($"  trained at club {c.Club?.DisplayName} period {c.Period}");
    return removed ? 0 : 1;
}

// proposalsurvey [type] [--all]   counts by type/status; a type name lists every FMRTE-visible offer of that type
int ProposalSurvey(string[] rest)
{
    bool all = rest.Contains("--all");
    string? typeFilter = rest.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
    using var session = Attach(writable: false);
    if (session.Profile.Proposals is null) { Console.WriteLine("profile has no proposal offsets"); return 2; }
    int players = 0, proposals = 0;
    var byStatus = new Dictionary<string, int>();
    foreach (var p in session.Players.LoadAll())
    {
        var list = session.Proposals.Read(p);
        if (list.Count == 0) continue;
        players++;
        proposals += list.Count;
        foreach (var x in list)
        {
            string key = $"{x.Type} {ProposalStatusName(x.Status)}";
            byStatus[key] = byStatus.GetValueOrDefault(key) + 1;
        }
    }
    int shown = 0;
    foreach (var p in session.Players.LoadAll())
    {
        foreach (var x in session.Proposals.Read(p).Where(x => TransferProposalReader.ShownByFmrte(x.Status)))
        {
            if (typeFilter is not null && !x.Type.ToString().Equals(typeFilter, StringComparison.OrdinalIgnoreCase)) continue;
            if (!all && shown >= 10) break;
            shown++;
            Console.WriteLine($"  shown: {p.DisplayName} {p.Id} {x.Type} {x.From} -> {x.To} fee {x.Value} status {ProposalStatusName(x.Status)} code {x.Status}");
        }
        if (!all && shown >= 10) break;
    }
    Console.WriteLine($"{proposals} proposals for {players} players");
    foreach (var (k, n) in byStatus.OrderByDescending(kv => kv.Value)) Console.WriteLine($"{n,6} {k}");
    return 0;
}

// Names as the app shows them (ClubLists.TransferOfferStatuses); 2/3/15/20 are the codes FMRTE hides.
static string ProposalStatusName(int status) => status switch
{
    8 => "Player accepted", 10 => "Unknown status 1", 12 => "Ready to sign", 19 => "Confirmed",
    2 or 3 or 15 or 20 => $"{status} (hidden by FMRTE)",
    _ => status.ToString(),
};

int ProposalSurveyRaw()
{
    using var session = Attach(writable: false);
    var m = session.Memory; ulong mb = session.Game.ModuleBase;
    ulong root = m.ReadUInt64(mb + 0x56F2EF0);
    if (root == 0) { Console.WriteLine("offer root is null; use proposalsurvey"); return 2; }
    var byId = new Dictionary<int, ulong>();
    for (ulong a = m.ReadUInt64(root + 8), e = m.ReadUInt64(root + 0x10); a < e; a += 8) { ulong o = m.ReadUInt64(a); byId.TryAdd(BitConverter.ToInt32(m.Read(o + 0x5C, 4)), o); }
    var seen = new Dictionary<string, int>(); int listed = 0, missing = 0;
    foreach (var p in session.Players.LoadAll())
    {
        var b = new byte[16];
        ulong h = m.ReadUInt64(p.Address + 0x320);
        if (h == 0 || !m.TryRead(h, b)) continue;
        ulong begin = BitConverter.ToUInt64(b, 0), end = BitConverter.ToUInt64(b, 8);
        if (begin == 0 || end < begin || (end - begin) % 4 != 0 || end - begin > 400) continue;
        foreach (var id in Enumerable.Range(0, (int)(end - begin) / 4).Select(i => BitConverter.ToInt32(m.Read(begin + (ulong)i * 4, 4))))
        {
            listed++;
            if (!byId.TryGetValue(id, out var o)) { missing++; continue; }
            var r = m.Read(o, 0x90);
            string key = $"vt +0x{BitConverter.ToUInt64(r, 0) - mb:X} status {r[0x8D]}";
            seen[key] = seen.GetValueOrDefault(key) + 1;
            if (seen[key] <= 2) Console.WriteLine($"{key}: {p.DisplayName} {p.Id} id {id} value {BitConverter.ToInt32(r, 0x58)}");
        }
    }
    Console.WriteLine($"{listed} listed ids, {missing} not in the offer list, {byId.Count} offers");
    foreach (var (k, n) in seen.OrderByDescending(kv => kv.Value)) Console.WriteLine($"{n,6} {k}");
    return 0;
}

// offerlist [name]   walks [module+0x56F2EF0]+0x8 (every transfer offer): vtable / status histogram, or one player's offers (read only)
int OfferList(string? name)
{
    using var session = Attach(writable: false);
    var m = session.Memory; ulong mb = session.Game.ModuleBase;
    var persons = session.Players.LoadAll().ToDictionary(p => p.Address + 0x288, p => p);
    var clubs = new Dictionary<ulong, string>();
    string Team(ulong t) { if (t == 0) return "-"; ulong c = m.ReadUInt64(t + 0x30); var b = new byte[8]; return c != 0 && m.TryRead(c + 0xC8, b) && BitConverter.ToUInt64(b) is var s && s != 0 ? System.Text.Encoding.UTF8.GetString(m.Read(s + 4, m.Read(s, 1)[0])) : $"team 0x{t:X}"; }
    ulong root = m.ReadUInt64(mb + 0x56F2EF0);
    ulong b0 = m.ReadUInt64(root + 8), e0 = m.ReadUInt64(root + 0x10);
    var hist = new Dictionary<string, int>();
    for (ulong a = b0; a < e0; a += 8)
    {
        ulong o = m.ReadUInt64(a); var r = m.Read(o, 0x98);
        ulong vt = BitConverter.ToUInt64(r, 0) - mb;
        persons.TryGetValue(BitConverter.ToUInt64(r, 0x38), out var p);
        string key = $"vt +0x{vt:X} status {r[0x8D]} b8 {r[8]}";
        hist[key] = hist.GetValueOrDefault(key) + 1;
        if (name == "samples" && p is not null && hist[key] <= 2) Console.WriteLine($"{key}: {p.DisplayName} {p.Id} {Team(BitConverter.ToUInt64(r, 0x50))} -> {Team(BitConverter.ToUInt64(r, 0x40))} {BitConverter.ToInt32(r, 0x58)}");
        if (name is not null && p is not null && p.DisplayName.Contains(name, StringComparison.OrdinalIgnoreCase))
            Console.WriteLine($"0x{o:X} {key} {p.DisplayName}: {Team(BitConverter.ToUInt64(r, 0x50))} -> {Team(BitConverter.ToUInt64(r, 0x40))} value {BitConverter.ToInt32(r, 0x58)} id {BitConverter.ToInt32(r, 0x5C)}{Environment.NewLine}   {Convert.ToHexString(r)}");
    }
    if (name is null) foreach (var (k, n) in hist.OrderByDescending(kv => kv.Value).Take(40)) Console.WriteLine($"{n,6} {k}");
    return 0;
}

// offerscan   heap objects whose +0x38 is a player's person and +0x40 and +0x50 are teams (FM26 TransferOffer layout); vtable histogram and samples (read only)
int OfferScan()
{
    using var session = Attach(writable: false);
    var m = session.Memory; ulong mb = session.Game.ModuleBase;
    var persons = session.Players.LoadAll().ToDictionary(p => p.Address + 0x288, p => p);
    ulong teamVt = mb + session.Profile.Team!.VtableRva;
    bool IsTeam(ulong a) => a != 0 && m.TryRead(a, new byte[8]) && m.ReadUInt64(a) == teamVt;
    var hist = new Dictionary<ulong, int>();
    var chunk = new byte[1 << 20];
    foreach (var region in session.Process.EnumerateWritableRegions(mb, session.Game.ModuleEnd).Where(r => !r.IsModuleData))
        for (ulong offset = 0; offset < region.Size; offset += (ulong)chunk.Length - 0x100)
        {
            int length = (int)Math.Min((ulong)chunk.Length, region.Size - offset);
            if (!m.TryRead(region.Start + offset, chunk.AsSpan(0, length))) continue;
            for (int k = 0; k + 0x98 <= length; k += 8)
            {
                if (!persons.TryGetValue(BitConverter.ToUInt64(chunk, k + 0x38), out var player)) continue;
                ulong buyer = BitConverter.ToUInt64(chunk, k + 0x40), seller = BitConverter.ToUInt64(chunk, k + 0x50);
                if (!IsTeam(buyer) && !IsTeam(seller)) continue;
                ulong vt = BitConverter.ToUInt64(chunk, k);
                hist[vt] = hist.GetValueOrDefault(vt) + 1;
                if (hist[vt] <= 3) Console.WriteLine($"0x{region.Start + offset + (ulong)k:X} vt +0x{vt - mb:X} {player.DisplayName} {player.Id}: {Convert.ToHexString(chunk, k, 0x98)}");
            }
        }
    foreach (var (vt, n) in hist.OrderByDescending(kv => kv.Value)) Console.WriteLine($"{n,6} vt 0x{vt:X} (+0x{vt - mb:X})");
    return 0;
}

// careerprobe <name>   writes plan 1's own fields back through the guarded writer (no value changes); shows whether the guard accepts it
int CareerProbe(string query)
{
    using var session = Attach(writable: true);
    var p = session.Players.LoadAll().First(x => x.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase));
    var owner = session.Owner(p);
    var plans = session.CareerPlans.Read(owner)!;
    var plan = plans[0];
    var item = session.CareerPlans.Item(plan.ItemAddress)!;
    Thread.Sleep(int.TryParse(Environment.GetEnvironmentVariable("PROBE_WAIT_MS"), out var w) ? w : 0);
    try { session.CareerPlanWriter.Set(owner, plans, plan, new CareerPlanFields(plan.Condition, plan.Scope, plan.Job, item)); Console.WriteLine("Set accepted"); }
    catch (Exception ex) { Console.WriteLine("Set refused: " + ex.Message); }
    var again = session.CareerPlans.Read(owner)!;
    Console.WriteLine($"same record bytes on second read: {again[0].Snapshot == plan.Snapshot}; begin/end same: {again[0].Begin == plan.Begin && again[0].End == plan.End}");
    return 0;
}

// careers <name>   career plans of players matching the name (read only)
// staffjobdept   which club department vector (medical / coaching / scouting) holds the staff of each job code, over every club (read only)
int StaffJobDepartments()
{
    using var session = Attach(writable: false);
    var m = session.Memory; var k = session.Profile.Club!;
    var clubs = session.LoadClubs(session.Players.LoadAll());
    var staff = session.Staff.LoadAll().Where(x => x.Club is not null).ToDictionary(x => x.StaffAddress, x => x);
    var map = new SortedDictionary<int, Dictionary<string, int>>();
    var departments = new[] { ("Medical", k.MedicalStaff), ("Coaching", k.CoachingStaff), ("Scouting", k.ScoutingStaff) };
    foreach (var club in clubs)
        foreach (var (name, at) in departments)
        {
            if (at is not { } off) continue;
            ulong begin = m.ReadUInt64(club.Address + (ulong)off), end = m.ReadUInt64(club.Address + (ulong)off + 8);
            if (begin == 0 || end < begin || (end - begin) / 8 > 400) continue;
            for (ulong p = begin; p < end; p += 8)
                if (staff.TryGetValue(m.ReadUInt64(p), out var member) && member.JobCode is { } job)
                {
                    if (!map.TryGetValue(job, out var d)) map[job] = d = [];
                    d[name] = d.GetValueOrDefault(name) + 1;
                }
        }
    foreach (var (job, d) in map) Console.WriteLine($"job {job,3}: {string.Join(", ", d.Select(x => $"{x.Key} {x.Value}"))}");
    return 0;
}

// relsurvey   histogram of relation record types over every player: count, up to 3 example players with the record bytes (read only)
int RelSurvey()
{
    using var session = Attach(writable: false);
    var m = session.Memory; var o = session.Profile.Relations!;
    var byType = new SortedDictionary<int, (int Count, List<string> Examples)>();
    foreach (var p in session.Players.LoadAll())
    {
        var owner = session.Owner(p);
        ulong holder = m.ReadUInt64(owner.At(o.Pointer));
        if (holder == 0) continue;
        ulong begin = m.ReadUInt64(holder), end = m.ReadUInt64(holder + 8);
        if (begin == 0 || end < begin || (end - begin) % (ulong)o.RecordSize != 0 || (end - begin) / (ulong)o.RecordSize > 500) continue;
        for (ulong a = begin; a < end; a += (ulong)o.RecordSize)
        {
            var r = m.Read(a, o.RecordSize);
            int type = r[o.Type];
            if (!byType.TryGetValue(type, out var e)) byType[type] = e = (0, []);
            e.Count++;
            if (e.Examples.Count < 3 && (e.Count == 1 || e.Count % 997 == 0)) e.Examples.Add($"{p.Id} {p.DisplayName}: {Convert.ToHexString(r)}");
            byType[type] = e;
        }
    }
    foreach (var (type, e) in byType) { Console.WriteLine($"type 0x{type:X2} ({type}): {e.Count}"); foreach (var x in e.Examples) Console.WriteLine("    " + x); }
    return 0;
}

// relplayer <id>   every relation record of one player: type, hex and the target object's vtable RVA (read only)
int RelPlayer(int id)
{
    using var session = Attach(writable: false);
    var m = session.Memory; var o = session.Profile.Relations!; ulong mb = session.Game.ModuleBase;
    var p = session.Players.LoadAll().Single(x => x.Id == id);
    var owner = session.Owner(p);
    ulong holder = m.ReadUInt64(owner.At(o.Pointer));
    ulong begin = m.ReadUInt64(holder), end = m.ReadUInt64(holder + 8);
    Console.WriteLine($"{p.DisplayName}: {(end - begin) / (ulong)o.RecordSize} records");
    for (ulong a = begin; a < end; a += (ulong)o.RecordSize)
    {
        var r = m.Read(a, o.RecordSize);
        ulong target = BitConverter.ToUInt64(r, 0);
        string vt = "-";
        if (target > 0x10000) { try { ulong v = m.ReadUInt64(target); vt = v >= mb ? $"+0x{v - mb:X}" : $"0x{v:X}"; } catch (Exception) { vt = "unreadable"; } }
        Console.WriteLine($"  type 0x{r[o.Type]:X2} {Convert.ToHexString(r)} target vtable {vt}");
    }
    return 0;
}

int CareersCommand(string query)
{
    using var session = Attach(writable: false);
    var reader = session.CareerPlans;
    foreach (var p in session.Players.LoadAll().Where(p => p.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(10))
    {
        var plans = reader.Read(p);
        Console.WriteLine($"{p.DisplayName} {p.Id}: {(plans is null ? "no container" : plans.Count + " plan(s)")}");
        foreach (var c in plans ?? []) Console.WriteLine($"  cond {c.Condition} scope {c.Scope} job {c.Job} type {c.ItemType} item {c.ItemName} 0x{c.Address:X}");
    }
    return 0;
}

// careersurvey   every player career plan (player+0x350 -> vector<plan*> at +0), histogram by field and item vtable (read only)
int CareerSurvey()
{
    using var session = Attach(writable: false);
    var m = session.Memory; ulong mb = session.Game.ModuleBase;
    var hist = new Dictionary<string, int>();
    int players = 0, plans = 0;
    foreach (var p in session.Players.LoadAll())
    {
        try {
        ulong c = m.ReadUInt64(p.Address + 0x350);
        if (c == 0) continue;
        ulong b = m.ReadUInt64(c), e = m.ReadUInt64(c + 8);
        if (b == 0 || e < b || (e - b) / 8 > 50) continue;
        players++;
        for (ulong a = b; a < e; a += 8)
        {
            var r = m.Read(m.ReadUInt64(a), 16); plans++;
            ulong item = BitConverter.ToUInt64(r, 0);
            string vt = item == 0 ? "null" : $"+0x{m.ReadUInt64(item) - mb:X}";
            string key = $"cond {r[8]} scope {r[9]} job {r[10]} type {r[11]} c {BitConverter.ToInt32(r, 12)} item {vt}";
            hist[key] = hist.GetValueOrDefault(key) + 1;
            if (hist[key] == 1) Console.WriteLine($"  e.g. {p.DisplayName} {p.Id}: {key} item 0x{item:X}");
        }
        } catch (MemoryAccessException) { hist["unreadable"] = hist.GetValueOrDefault("unreadable") + 1; }
    }
    Console.WriteLine($"{players} players with a container, {plans} plans");
    foreach (var (k, n) in hist.OrderByDescending(kv => kv.Value).Take(40)) Console.WriteLine($"{n,7} {k}");
    return 0;
}

// pfhist <PlayerField> [player id]   histogram of a mapped player field over every loaded player, or one player's value (read only)
int PlayerFieldHistogram(PlayerField field, int id)
{
    using var session = Attach(writable: false);
    var access = new PlayerFieldAccess(session.Memory, session.Profile, session.Game.ModuleBase);
    var players = session.Players.LoadAll();
    if (id != 0) { foreach (var p in players.Where(p => p.Id == id)) Console.WriteLine($"{p.Id} {p.DisplayName}: {access.Read(p, field)}"); return 0; }
    foreach (var g in players.GroupBy(p => access.Read(p, field)).OrderByDescending(g => g.Count()).Take(40))
        Console.WriteLine($"{g.Key,12} {g.Count(),7}  e.g. {string.Join(", ", g.Take(3).Select(p => $"{p.DisplayName} {p.Id}"))}");
    return 0;
}

int RelTypeCommand(int type)
{
    using var session = Attach(writable: false);
    var o = session.Profile.Relations!;
    int count = 0;
    var q = new byte[8];
    bool Q(ulong at, out ulong v) { bool ok = at != 0 && session.Memory.TryRead(at, q); v = ok ? BitConverter.ToUInt64(q) : 0; return ok; }
    foreach (var p in session.Players.LoadAll())
    {
        if (!Q(p.Address + (ulong)o.Pointer, out ulong holder) || holder == 0) continue;
        if (!Q(holder, out ulong begin) || !Q(holder + 8, out ulong end) || begin == 0 || end < begin || end - begin > 500 * 16) continue;
        var bytes = session.Memory.Read(begin, (int)(end - begin));
        for (int k = 0; k + 16 <= bytes.Length; k += 16)
            if (bytes[k + o.Type] == type) { count++; Console.WriteLine($"{p.Id} | {p.DisplayName} | {p.Club?.DisplayName} | {Convert.ToHexString(bytes, k, 16)}"); }
    }
    Console.WriteLine($"{count} records of type 0x{type:X}");
    return 0;
}

// nearptr <target> <hex>...   every heap qword equal to target with one of the hex byte patterns within ±0x40 (read only)
int NearPtrCommand(ulong target, string[] patterns)
{
    using var session = Attach(writable: false);
    var needles = patterns.Select(Convert.FromHexString).ToArray();
    var chunk = new byte[1 << 20]; int hits = 0, refs = 0;
    foreach (var region in session.Process.EnumerateWritableRegions(session.Game.ModuleBase, session.Game.ModuleEnd).Where(r => !r.IsModuleData))
        for (ulong offset = 0; offset < region.Size; offset += (ulong)chunk.Length - 0x80)
        {
            int length = (int)Math.Min((ulong)chunk.Length, region.Size - offset);
            if (!session.Memory.TryRead(region.Start + offset, chunk.AsSpan(0, length))) continue;
            for (int k = 0x40; k + 0x48 <= length; k += 8)
            {
                if (BitConverter.ToUInt64(chunk, k) != target) continue;
                refs++;
                var window = chunk.AsSpan(k - 0x40, 0x88).ToArray();
                if (needles.Length > 0 && !needles.Any(n => window.AsSpan().IndexOf(n) >= 0)) continue;
                hits++;
                Console.WriteLine($"0x{region.Start + offset + (ulong)k:X}  -0x40: {Convert.ToHexString(window[..0x40])}\n    ptr+: {Convert.ToHexString(window[0x40..])}");
            }
        }
    Console.WriteLine($"{refs} refs, {hits} with pattern");
    return 0;
}

// Read-only candidate scan based on the historical FM26 WorkPermit layout; results are hypotheses, not mappings.
int PermitProbeCommand()
{
    using var session = Attach(writable: false);
    var memory = session.Memory;
    var nations = session.Nations.LoadAll();
    var nationKeys = nations.SelectMany(n => new[] { n.Id ?? -1, BitConverter.ToInt32(memory.Read(n.Address + 8, 4)) }).Where(x => x >= 0).ToHashSet();
    var players = session.Players.LoadAll();
    var personKeys = players.SelectMany((p, index) => new[] { index, p.Id ?? -1, BitConverter.ToInt32(memory.Read(p.Address + 8, 4)), BitConverter.ToInt32(memory.Read(p.Address + 0x290, 4)) }).Where(x => x > 0).ToHashSet();
    var chunk = new byte[1 << 20]; long scanned = 0; int candidates = 0;
    foreach (var region in session.Process.EnumerateWritableRegions(session.Game.ModuleBase, session.Game.ModuleEnd).Where(r => !r.IsModuleData))
        for (ulong offset = 0; offset < region.Size; offset += (ulong)chunk.Length - 0x20)
        {
            int length = (int)Math.Min((ulong)chunk.Length, region.Size - offset);
            if (!memory.TryRead(region.Start + offset, chunk.AsSpan(0, length))) continue;
            scanned += length;
            for (int k = 0; k + 0x20 <= length; k += 8)
            {
                int sy = BitConverter.ToUInt16(chunk, k + 0x12), ey = BitConverter.ToUInt16(chunk, k + 0x16);
                int sd = BitConverter.ToUInt16(chunk, k + 0x10) & 511, ed = BitConverter.ToUInt16(chunk, k + 0x14) & 511;
                if (sy is < 2020 or > 2026 || ey < 2026 || ey > 2040 || sd is < 1 or > 366 || ed is < 1 or > 366 || sy > ey) continue;
                int nation = BitConverter.ToInt32(chunk, k), person = BitConverter.ToInt32(chunk, k + 8);
                if (!nationKeys.Contains(nation) || !personKeys.Contains(person)) continue;
                candidates++;
                Console.WriteLine($"candidate 0x{region.Start + offset + (ulong)k:X} nation-key {nation} person-key {person} dates {sy}/{sd}..{ey}/{ed} raw {Convert.ToHexString(chunk.AsSpan(k, 0x20))}");
            }
        }
    Console.WriteLine($"Hypothesis scan: {scanned:N0} bytes, {players.Count} players, {nations.Count} nations; {candidates} candidates. Not proof of presence or absence of work permits.");
    return 0;
}

int BanMatchesCommand(int playerId, ulong address, int matches, int fulfilled)
{
    using var session = Attach(writable: true);
    var player = session.Players.LoadAll().Single(p => p.Id == playerId);
    var ban = session.ReadBans(player).Single(b => b.Address == address);
    new BanWriter(session.Memory, session.Profile, session.Game.ModuleBase).SetMatches(player, ban, matches, fulfilled);
    Console.WriteLine($"{player.Id}: {ban.MatchesFulfilled}/{ban.Matches} -> {fulfilled}/{matches}");
    return 0;
}

int InjuryDurationCommand(int playerId, ulong address, int minimum, int extra)
{
    using var session = Attach(writable: true);
    var player = session.Players.LoadAll().Single(p => p.Id == playerId);
    var injury = session.ReadInjuries(player).Single(i => i.Address == address);
    new InjuryWriter(session.Memory, session.Profile, session.Game.ModuleBase).SetDuration(player, injury, minimum, extra);
    Console.WriteLine($"{player.Id}: {injury.MinimumDays}+{injury.ExtraDays} -> {minimum}+{extra}");
    return 0;
}

int SquadCommand(int clubId)
{
    using var session = Attach(writable: false);
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId);
    if (club?.FirstTeam is not { } team) { Console.Error.WriteLine($"no club or first team for id {clubId}"); return 2; }
    var squad = session.ReadSquad(team.Address);
    Console.WriteLine($"{club.Name}: {squad.Count} players");
    foreach (var p in squad)
    {
        var v = session.PlayerFields.ReadAll(p);
        string F(Fm27Editor.Core.Players.PlayerField f) => v.TryGetValue(f, out var x) ? x.ToString() : "-";
        var injuries = session.ReadInjuries(p);
        string inj = injuries.Count == 0 ? "" : "  injured: " + string.Join(", ", injuries.Select(i => $"{i.Name} {i.MinimumDays}+{i.ExtraDays}d"));
        Console.WriteLine($"  {p.DisplayName,-24} CA {p.CurrentAbility,3} cond {F(Fm27Editor.Core.Players.PlayerField.Condition),5} sharp {F(Fm27Editor.Core.Players.PlayerField.MatchSharpness),5} jaded {F(Fm27Editor.Core.Players.PlayerField.Jadedness),5} morale {F(Fm27Editor.Core.Players.PlayerField.Morale),2} price {F(Fm27Editor.Core.Players.PlayerField.AskingPrice),9} caps {F(Fm27Editor.Core.Players.PlayerField.Caps)}{inj}");
    }
    return 0;
}

// vision <club-id> [set <index> target|status <value> --write]
int VisionCommand(int clubId, string[] rest)
{
    using var session = Attach(writable: rest.Contains("--write"));
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var team = club.Teams[0];
    var rows = session.ReadClubVision(team.Address, null);
    Console.WriteLine($"{club.Name}: {rows.Count} rows");
    for (int i = 0; i < rows.Count; i++)
    {
        var r = rows[i];
        Console.WriteLine($"  [{i,2}] type {r.TypeCode,3} importance {r.Importance,3} target {r.Target?.ToString() ?? "-",5} " +
                          $"source {r.SourceType?.ToString() ?? "-",3} status {r.Status?.ToString() ?? "-",2} " +
                          $"{r.StartDate:yyyy-MM-dd}..{r.EndDate:yyyy-MM-dd} {r.ClubItem?.DisplayName}");
    }

    var words = rest.Where(w => w != "--write").ToArray();
    if (words.Length >= 2 && words[0].Equals("remove", StringComparison.OrdinalIgnoreCase))
    {
        int victim = int.Parse(words[1]);
        if (victim < 0 || victim >= rows.Count) { Console.Error.WriteLine($"row {victim} is out of range"); return 2; }
        Console.WriteLine($"remove row {victim}: type {rows[victim].TypeCode} importance {rows[victim].Importance}");
        if (!rest.Contains("--write")) return 0;
        session.ClubVisionWriter.Remove(team.Address, rows[victim]);
        Console.WriteLine($"removed; now {session.ReadClubVision(team.Address, null).Count} rows");
        return 0;
    }
    if (words.Length < 4 || !words[0].Equals("set", StringComparison.OrdinalIgnoreCase)) return 0;
    int index = int.Parse(words[1]);
    if (index < 0 || index >= rows.Count) { Console.Error.WriteLine($"row {index} is out of range"); return 2; }
    var row = rows[index];
    string what = words[2].ToLowerInvariant();
    if (what == "target")
    {
        int value = int.Parse(words[3]);
        Console.WriteLine($"target row {index}: {row.Target} -> {value}");
        if (!rest.Contains("--write")) return 0;
        session.ClubVisionWriter.SetTarget(team.Address, row, value);
    }
    else if (what == "status")
    {
        byte value = byte.Parse(words[3]);
        Console.WriteLine($"status row {index}: {row.Status} -> {value}");
        if (!rest.Contains("--write")) return 0;
        session.ClubVisionWriter.SetStatus(team.Address, row, value);
    }
    else if (what == "source")
    {
        int value = int.Parse(words[3]);
        Console.WriteLine($"source row {index}: {row.SourceType} -> {value}");
        if (!rest.Contains("--write")) return 0;
        session.ClubVisionWriter.SetSourceType(team.Address, row, value);
    }
    else if (what == "type")
    {
        byte value = byte.Parse(words[3]);
        Console.WriteLine($"type row {index}: {row.TypeCode} -> {value}");
        if (!rest.Contains("--write")) return 0;
        session.ClubVisionWriter.SetType(team.Address, row, value);
    }
    else if (what == "importance")
    {
        int value = int.Parse(words[3]);
        Console.WriteLine($"importance row {index}: {row.Importance} -> {value}");
        if (!rest.Contains("--write")) return 0;
        session.ClubVisionWriter.SetImportance(team.Address, row, value);
    }
    else if (what == "dates")
    {
        if (words.Length < 5) { Console.Error.WriteLine("dates takes <start> <end> as yyyy-MM-dd"); return 2; }
        var start = DateOnly.ParseExact(words[3], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var end = DateOnly.ParseExact(words[4], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        Console.WriteLine($"dates row {index}: {row.StartDate:yyyy-MM-dd}..{row.EndDate:yyyy-MM-dd} -> {start:yyyy-MM-dd}..{end:yyyy-MM-dd}");
        if (!rest.Contains("--write")) return 0;
        session.ClubVisionWriter.SetDates(team.Address, row, start, end);
    }
    else { Console.Error.WriteLine("field must be source, type, importance, target, status or dates"); return 2; }
    var after = session.ReadClubVision(team.Address, null)[index];
    Console.WriteLine($"written; read back target {after.Target} status {after.Status}");
    return 0;
}

// visionscan   histograms for the unknown dwords/bytes of culture records, split by "already ended"
int VisionScanCommand()
{
    using var session = Attach(writable: false);
    var today = session.ReadGameDate() ?? DateOnly.FromDateTime(DateTime.UtcNow);
    var clubs = session.LoadClubs(session.Players.LoadAll());
    var d34 = new SortedDictionary<int, int>();
    var d38 = new SortedDictionary<int, int>();
    var b3c = new SortedDictionary<byte, int>();
    var b39 = new SortedDictionary<byte, int>();
    var d38Ended = new SortedDictionary<int, int>();
    int rows = 0;
    foreach (var club in clubs)
        foreach (var team in club.Teams)
        {
            IReadOnlyList<Fm27Editor.Core.Clubs.ClubVisionEntry> list;
            try { list = session.ReadClubVision(team.Address); } catch { continue; }
            foreach (var row in list)
            {
                var raw = session.Memory.Read(row.Address, 0x50);
                if (raw.Length < 0x40) continue;
                rows++;
                Bump(d34, BitConverter.ToInt32(raw, 0x34));
                Bump(d38, BitConverter.ToInt32(raw, 0x38));
                Bump(b3c, raw[0x3C]);
                Bump(b39, raw[0x39]);
                if (row.EndDate is { } end && end < today) Bump(d38Ended, BitConverter.ToInt32(raw, 0x38));
            }
        }
    Console.WriteLine($"game date {today:yyyy-MM-dd}, {rows:N0} rows");
    Dump("i32@0x34", d34);
    Dump("i32@0x38", d38);
    Dump("i32@0x38 (already ended)", d38Ended);
    Dump("b@0x39", b39);
    Dump("b@0x3C", b3c);
    return 0;

    static void Bump<TKey>(SortedDictionary<TKey, int> map, TKey key) where TKey : notnull
    {
        map.TryGetValue(key, out int n);
        map[key] = n + 1;
    }

    static void Dump<TKey>(string title, SortedDictionary<TKey, int> map) where TKey : notnull =>
        Console.WriteLine($"{title}: " + string.Join(", ", map.Select(p => $"{p.Key}:{p.Value:N0}")));
}

// visionstatus   correlate record byte +0x38 with whether the row is still running, across every loaded club
int VisionStatusCommand()
{
    using var session = Attach(writable: false);
    var today = session.ReadGameDate() ?? DateOnly.FromDateTime(DateTime.UtcNow);
    Console.WriteLine($"game date {today:yyyy-MM-dd}");
    var clubs = session.LoadClubs(session.Players.LoadAll());
    var byStatus = new SortedDictionary<byte, int>();
    var expiredByStatus = new SortedDictionary<byte, int>();
    int rows = 0, unreadable = 0;
    foreach (var club in clubs)
    {
        foreach (var team in club.Teams)
        {
            IReadOnlyList<Fm27Editor.Core.Clubs.ClubVisionEntry> list;
            try { list = session.ReadClubVision(team.Address); }
            catch { unreadable++; continue; }
            foreach (var row in list)
            {
                rows++;
                var raw = session.Memory.Read(row.Address, 0x50);
                if (raw.Length < 0x40) continue;
                byte status = raw[0x38];
                byStatus.TryGetValue(status, out int count);
                byStatus[status] = count + 1;
                if (row.EndDate is { } end && end < today)
                {
                    expiredByStatus.TryGetValue(status, out int expired);
                    expiredByStatus[status] = expired + 1;
                }
            }
        }
    }
    Console.WriteLine($"{clubs.Count} clubs, {rows:N0} vision rows ({unreadable} teams unreadable)");
    Console.WriteLine("byte +0x38 distribution: " + string.Join(", ", byStatus.Select(p => $"{p.Key}:{p.Value:N0}")));
    Console.WriteLine("of which already ended:   " + string.Join(", ", expiredByStatus.Select(p => $"{p.Key}:{p.Value:N0}")));
    return 0;
}

// visionraw <club-id> <row> <offset-hex> <bytes-hex> [--write]   ghi thô để thử một field chưa rõ
int VisionRawCommand(int clubId, string[] rest)
{
    bool write = rest.Contains("--write");
    var w = rest.Where(a => a != "--write").ToArray();
    if (w.Length < 3) { Console.Error.WriteLine("usage: visionraw <clubId> <row> <offset> <hex> [--write]"); return 2; }
    int rowIndex = int.Parse(w[0]);
    int offset = Convert.ToInt32(w[1].StartsWith("0x") ? w[1][2..] : w[1], 16);
    byte[] value = Convert.FromHexString(w[2]);
    using var session = Attach(writable: write);
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var rows = session.ReadClubVision(club.Teams[0].Address, null);
    if (rowIndex < 0 || rowIndex >= rows.Count) { Console.Error.WriteLine($"club has {rows.Count} rows"); return 2; }
    var row = rows[rowIndex];
    var before = session.Memory.Read(row.Address + (ulong)offset, value.Length);
    Console.WriteLine($"row {rowIndex} +0x{offset:X}: {Convert.ToHexString(before)} -> {Convert.ToHexString(value)}" + (write ? "" : " (plan only)"));
    if (!write) return 0;
    session.Memory.TryWrite(row.Address + (ulong)offset, value);
    var after = session.Memory.Read(row.Address + (ulong)offset, value.Length);
    Console.WriteLine($"read back {Convert.ToHexString(after)} ({(after.AsSpan().SequenceEqual(value) ? "matches" : "MISMATCH")})");
    return 0;
}

// deals [--at 0xaddr]   list transfer-deal objects (heap scan by vtable) and print their decoded fields
int DealsCommand(string[] rest)
{
    using var session = Attach(writable: false);
    var reader = session.TransferDeals;
    var people = session.Players.LoadAll().ToDictionary(p => p.Address + 0x288, p => p);
    IReadOnlyList<TransferDeal> deals;
    if (rest.Length >= 2 && rest[0].Equals("--at", StringComparison.OrdinalIgnoreCase))
    {
        var address = rest[1].StartsWith("0x") ? Convert.ToUInt64(rest[1][2..], 16) : ulong.Parse(rest[1]);
        var one = reader.Read(address);
        deals = one is null ? [] : [one];
    }
    else deals = reader.FindAll(people.Keys.ToHashSet());

    Console.WriteLine($"deals found: {deals.Count}");
    foreach (var deal in deals.Take(60))
    {
        people.TryGetValue(deal.PersonAddress, out var player);
        Console.WriteLine($"  0x{deal.Address:X} {player?.DisplayName} {player?.Id} " +
                          $"{reader.TeamName(deal.SellingTeam) ?? "?"} -> {reader.TeamName(deal.BuyingTeam) ?? "?"} " +
                          $"fee {deal.Fee:N0} status {deal.Status} day {deal.DayCounter}");
    }
    return 0;
}

// visionitems <club-id>   item reference of every club-vision row: index, marker word, resolved name
int VisionItemsCommand(string[] rest)
{
    if (rest.Length < 1) { Console.Error.WriteLine("usage: visionitems <clubId>"); return 2; }
    using var session = Attach(writable: false);
    var players = session.Players.LoadAll();
    var club = session.LoadClubs(players).SingleOrDefault(c => c.Id == int.Parse(rest[0]));
    if (club is null) { Console.Error.WriteLine($"no club {rest[0]}"); return 2; }
    var competitionNames = session.LoadCompetitions(players)
        .ToDictionary(kv => kv.Key, kv => kv.Value.Name ?? kv.Value.ShortName ?? "");
    var clubs = session.LoadClubs(players);
    var tournaments = new Dictionary<int, string>(session.ScanCompetitionNames());
    foreach (var kv in competitionNames) tournaments.TryAdd(kv.Key, kv.Value);
    var nations = session.Nations.LoadAll();
    var nationsById = nations.Select((n, i) => (i, n.Name)).ToDictionary(x => x.i, x => x.Name ?? "");
    var rows = session.ReadClubVision(club.Teams[0].Address, clubs, tournaments, nationsById, null);
    Console.WriteLine($"profile {session.Profile.Build} variant={session.Profile.Variant} root={session.Profile.Competition?.ListRootRva:X}");
    Console.WriteLine($"{club.Name}: {rows.Count} rows, {competitionNames.Count} player comps, {tournaments.Count} competitions, {nations.Count} nations");
    foreach (var r in rows)
    {
        var raw = session.Memory.Read(r.Address + 8, 4);
        Console.WriteLine($"  0x{r.Address:X} type {r.TypeCode,3} imp {r.Importance,3} idx {r.ItemIndex,6} marker 0x{BitConverter.ToUInt16(raw, 2):X4} " +
                          $"{r.ItemKind,-11} '{r.ItemName}' target {r.Target}");
    }
    return 0;
}

// dealfee <player name|id> <fee> [--write]   change one player's live offer fee (guarded)
int DealFeeCommand(string[] rest)
{
    if (rest.Length < 2) { Console.Error.WriteLine("usage: dealfee <player> <fee> [--write]"); return 2; }
    bool write = rest.Any(a => a.Equals("--write", StringComparison.OrdinalIgnoreCase));
    using var session = Attach(writable: write);
    var deal = FindDealOf(session, rest[0]);
    if (deal is null) { Console.Error.WriteLine($"no deal for {rest[0]}"); return 2; }
    int fee = int.Parse(rest[1]);
    Console.WriteLine($"deal 0x{deal.Address:X} fee {deal.Fee:N0} status {deal.Status}");
    if (!write) { Console.WriteLine($"would set fee {fee:N0}"); return 0; }
    new TransferDealWriter(session.Memory, session.Profile, session.Game.ModuleBase).SetFee(deal, deal.Fee, fee);
    var after = session.TransferDeals.Read(deal.Address);
    Console.WriteLine($"fee now {after!.Fee:N0}");
    return 0;
}

// dealstatus <player name|id> <label|hex> [--write]   change one player's live offer status (guarded)
int DealStatusCommand(string[] rest)
{
    if (rest.Length < 2) { Console.Error.WriteLine("usage: dealstatus <player> <offer-made|accepted|player-accepted|rejected|cancelled> [--write]"); return 2; }
    bool write = rest.Any(a => a.Equals("--write", StringComparison.OrdinalIgnoreCase));
    using var session = Attach(writable: write);
    var deal = FindDealOf(session, rest[0]);
    if (deal is null) { Console.Error.WriteLine($"no deal for {rest[0]}"); return 2; }
    int status = rest[1].ToLowerInvariant() switch
    {
        "offer-made" => TransferDealStatus.OfferMade,
        "accepted" or "offer-accepted" => TransferDealStatus.OfferAccepted,
        "player-accepted" => TransferDealStatus.PlayerAccepted,
        "rejected" => TransferDealStatus.Rejected,
        "cancelled" => TransferDealStatus.Cancelled,
        _ => Convert.ToInt32(rest[1].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? rest[1][2..] : rest[1], 16),
    };
    Console.WriteLine($"deal 0x{deal.Address:X} fee {deal.Fee:N0} status {deal.Status}");
    if (!write) { Console.WriteLine($"would set status {TransferDealStatus.Label(status)}"); return 0; }
    new TransferDealWriter(session.Memory, session.Profile, session.Game.ModuleBase).SetStatus(deal, deal.StatusRaw, status);
    var after = session.TransferDeals.Read(deal.Address);
    Console.WriteLine($"status now {after!.Status}");
    return 0;
}

TransferDeal? FindDealOf(GameSession session, string key)
{
    var players = session.Players.LoadAll();
    var player = players.FirstOrDefault(p => p.Id.ToString() == key)
                 ?? players.FirstOrDefault(p => p.DisplayName.Equals(key, StringComparison.OrdinalIgnoreCase));
    if (player is null) return null;
    return session.TransferDeals.FindForPlayer(player.Address + 0x288);
}

// regunlock <check|install|status|uninstall> [rvaHex]   guarded trampoline that captures the squad-registration rules object
int RegUnlockCommand(string[] rest)
{
    if (rest.Length < 1) { Console.Error.WriteLine("usage: regunlock check|install|status|uninstall [rvaHex]"); return 2; }
    int? rva = rest.Length > 1 && rest[1].StartsWith("0x") ? Convert.ToInt32(rest[1][2..], 16) : null;
    int waitMs = 20000;
    int waitIndex = Array.FindIndex(rest, a => a.Equals("--wait", StringComparison.OrdinalIgnoreCase));
    if (waitIndex >= 0 && waitIndex + 1 < rest.Length) waitMs = int.Parse(rest[waitIndex + 1]) * 1000;
    bool write = rest[0] is "install" or "uninstall" or "probe";
    using var session = Attach(writable: write);
    var unlock = new Fm27Editor.Core.Clubs.RegistrationUnlock(session.Memory, session.Profile, session.Game.ModuleBase,
        rva ?? 0x5270FA);
    switch (rest[0].ToLowerInvariant())
    {
        case "check":
        {
            var bytes = session.Memory.Read(unlock.Target, 7);
            Console.WriteLine($"site 0x{unlock.Target:X} bytes {Convert.ToHexString(bytes)}");
            Console.WriteLine(Convert.ToHexString(bytes) == Convert.ToHexString(Fm27Editor.Core.Clubs.RegistrationUnlock.StolenBytes)
                ? "matches the registration prologue" : "does not match");
            return 0;
        }
        case "probe":
        {
            unlock.Install();
            Console.WriteLine($"installed at 0x{unlock.Target:X}; waiting {waitMs / 1000} s for the game to call it");
            System.Threading.Thread.Sleep(waitMs);
            bool ok = unlock.TryCaptureRules(out ulong captured);
            Console.WriteLine($"captured {ok} rules 0x{captured:X}");
            if (ok)
            {
                var head = session.Memory.Read(captured, 0x40);
                Console.WriteLine("rules head " + Convert.ToHexString(head));
            }
            unlock.Uninstall();
            Console.WriteLine("uninstalled");
            return 0;
        }
        case "install":
            unlock.Install();
            Console.WriteLine($"installed at 0x{unlock.Target:X}");
            return 0;
        case "status":
        {
            bool captured = unlock.TryCaptureRules(out ulong rules);
            Console.WriteLine($"installed {unlock.IsInstalled} captured {captured} rules 0x{rules:X}");
            return 0;
        }
        case "uninstall":
            unlock.Uninstall();
            Console.WriteLine("uninstalled");
            return 0;
    }
    Console.Error.WriteLine("unknown action");
    return 2;
}
// dumpmod <rvaHex> <lenHex> <outFile>   write a slice of the game module to disk for a disassembler
int DumpModuleCommand(string[] rest)
{
    if (rest.Length < 3) { Console.Error.WriteLine("usage: dumpmod <rvaHex> <lenHex> <outFile>"); return 2; }
    ulong rva = Convert.ToUInt64(rest[0].Replace("0x", ""), 16);
    int len = Convert.ToInt32(rest[1].Replace("0x", ""), 16);
    using var session = Attach(writable: false);
    var bytes = session.Memory.Read(session.Game.ModuleBase + rva, len);
    File.WriteAllBytes(rest[2], bytes);
    Console.WriteLine($"{len} bytes at module+0x{rva:X} -> {rest[2]}");
    return 0;
}
// objstr <addrHex> <sizeHex>   print every field of an object that resolves to a count-prefixed UTF-8 string
int ObjStrCommand(string[] rest)
{
    if (rest.Length < 2) { Console.Error.WriteLine("usage: objstr <addrHex> <sizeHex>"); return 2; }
    ulong address = Convert.ToUInt64(rest[0].Replace("0x", ""), 16);
    int size = Convert.ToInt32(rest[1].Replace("0x", ""), 16);
    using var session = Attach(writable: false);
    var memory = session.Memory;
    string? Text(ulong p)
    {
        if (p < 0x10000) return null;
        Span<byte> len = stackalloc byte[4];
        if (!memory.TryRead(p, len)) return null;
        int count = BitConverter.ToInt32(len);
        if (count is < 2 or > 200) return null;
        var bytes = new byte[count];
        if (!memory.TryRead(p + 4, bytes)) return null;
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        return text.All(ch => ch >= ' ' && ch != '\uFFFD') ? text : null;
    }
    var b = memory.Read(address, size);
    for (int o = 0; o + 8 <= size; o += 8)
    {
        ulong p = BitConverter.ToUInt64(b, o);
        if (p < 0x10000 || p > 0x7FF000000000) continue;
        if (Text(p) is not { } s) continue;
        Console.WriteLine($"  +0x{o:X2} 0x{p:X} \"{s}\"");
    }
    return 0;
}
// compvec2 <slotHex>   find the pointer vector a slot belongs to: elements must point at objects sharing the slot's vtable
int CompVec2Command(string[] rest)
{
    if (rest.Length < 1) { Console.Error.WriteLine("usage: compvec2 <slotHex>"); return 2; }
    ulong slot = Convert.ToUInt64(rest[0].Replace("0x", ""), 16);
    using var session = Attach(writable: false);
    var memory = session.Memory;
    ulong Q(ulong a) { Span<byte> b = stackalloc byte[8]; return memory.TryRead(a, b) ? BitConverter.ToUInt64(b) : 0; }
    ulong first = Q(slot);
    ulong vt = first == 0 ? 0 : Q(first);
    Console.WriteLine($"slot 0x{slot:X} -> object 0x{first:X} vtable 0x{vt:X}");
    if (vt == 0) return 2;
    var hits = new List<(ulong Begin, ulong End)>();
    for (ulong back = 0; back <= 0x800000; back += 8)
    {
        ulong begin = slot - back;
        ulong e = Q(begin + 8);
        if (e <= begin || (e - begin) % 8 != 0 || (e - begin) / 8 is < 2 or > 100000) continue;
        ulong head = Q(begin);
        if (head == 0 || Q(head) != vt) continue;
        ulong tail = Q(e - 8);
        if (tail == 0 || Q(tail) != vt) continue;
        hits.Add((begin, e));
        Console.WriteLine($"  begin 0x{begin:X} end 0x{e:X} count {(e - begin) / 8}");
        if (hits.Count >= 5) break;
    }
    Console.WriteLine($"{hits.Count} candidate vectors");
    return 0;
}

// compvec <elementHex> <strideHex>   find the vector a pseudo-object belongs to: a begin pointer on the element grid
int CompVecCommand(string[] rest)
{
    if (rest.Length < 2) { Console.Error.WriteLine("usage: compvec <elementHex> <strideHex>"); return 2; }
    ulong element = Convert.ToUInt64(rest[0].Replace("0x", ""), 16);
    ulong stride = Convert.ToUInt64(rest[1].Replace("0x", ""), 16);
    using var session = Attach(writable: false);
    var memory = session.Memory;
    ulong vt = memory.ReadUInt64(element);
    Console.WriteLine($"element 0x{element:X} vtable 0x{vt:X} stride 0x{stride:X}");
    var chunk = new byte[1 << 22];
    var hits = new List<(ulong Begin, ulong End)>();
    foreach (var region in session.Process.EnumerateWritableRegions(session.Game.ModuleBase, session.Game.ModuleEnd).Where(r => !r.IsModuleData))
        for (ulong offset = 0; offset + 8 <= region.Size; offset += (ulong)chunk.Length)
        {
            int length = (int)Math.Min((ulong)chunk.Length, region.Size - offset);
            if (!memory.TryRead(region.Start + offset, chunk.AsSpan(0, length))) break;
            for (int i = 0; i + 8 <= length; i += 8)
            {
                ulong v = BitConverter.ToUInt64(chunk, i);
                if (v == 0 || v > element || element - v > 0x400000 || (element - v) % stride != 0) continue;
                var pair = new byte[8];
                if (!memory.TryRead(v + 8, pair)) continue;
                ulong e = BitConverter.ToUInt64(pair);
                if (e <= v || (e - v) % stride != 0 || (e - v) / stride is < 2 or > 100000) continue;
                var head = new byte[8];
                if (!memory.TryRead(v, head) || BitConverter.ToUInt64(head) != vt) continue;
                hits.Add((v, e));
                Console.WriteLine($"  vector begin 0x{v:X} end 0x{e:X} count {(e - v) / stride} (slot at 0x{region.Start + offset + (ulong)i:X})");
            }
        }
    Console.WriteLine($"{hits.Count} candidate vectors");
    return 0;
}

// scanpat <hexbytes>   every occurrence of a byte pattern inside the game module, printed as an RVA (read only)
int ScanPatternCommand(string[] rest)
{
    if (rest.Length < 1) { Console.Error.WriteLine("usage: scanpat <hex>"); return 2; }
    var needle = Convert.FromHexString(rest[0].Replace(" ", "").Replace("0x", ""));
    if (needle.Length < 3) { Console.Error.WriteLine("pattern too short"); return 2; }
    using var session = Attach(writable: false);
    var memory = session.Memory;
    ulong start = session.Game.ModuleBase, end = session.Game.ModuleEnd;
    var buffer = new byte[1 << 22];
    int hits = 0;
    for (ulong offset = 0; offset < end - start; offset += (ulong)buffer.Length - (ulong)needle.Length)
    {
        int length = (int)Math.Min((ulong)buffer.Length, end - start - offset);
        if (!memory.TryRead(start + offset, buffer.AsSpan(0, length))) continue;
        var span = buffer.AsSpan(0, length);
        int at = 0;
        while ((at = span[at..].IndexOf(needle)) >= 0)
        {
            ulong address = start + offset + (ulong)at;
            Console.WriteLine($"  hit RVA +0x{address - start:X}");
            hits++;
            at += needle.Length;
        }
    }
    Console.WriteLine($"{hits} hits in module 0x{start:X}..0x{end:X}");
    return 0;
}

// nearrange <loHex> <hiHex>   every qword in writable memory that points into [lo, hi) — finds an object holding a struct
int NearRangeCommand(string[] rest)
{
    if (rest.Length < 2) { Console.Error.WriteLine("usage: nearrange <loHex> <hiHex> [maxHits]"); return 2; }
    ulong lo = Convert.ToUInt64(rest[0].Replace("0x", ""), 16);
    ulong hi = Convert.ToUInt64(rest[1].Replace("0x", ""), 16);
    int maxHits = rest.Length > 2 ? int.Parse(rest[2]) : 40;
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var chunk = new byte[1 << 22];
    var hits = new List<(ulong At, ulong Value)>();
    foreach (var region in session.Process.EnumerateWritableRegions(session.Game.ModuleBase, session.Game.ModuleEnd).Where(r => !r.IsModuleData))
        for (ulong offset = 0; offset + 8 <= region.Size; offset += (ulong)chunk.Length)
        {
            int length = (int)Math.Min((ulong)chunk.Length, region.Size - offset);
            if (!mem.TryRead(region.Start + offset, chunk.AsSpan(0, length))) break;
            for (int i = 0; i + 8 <= length; i += 8)
            {
                ulong v = BitConverter.ToUInt64(chunk, i);
                if (v < lo || v >= hi) continue;
                hits.Add((region.Start + offset + (ulong)i, v));
                if (hits.Count >= maxHits) goto done;
            }
        }
done:
    Console.WriteLine($"{hits.Count} pointers into 0x{lo:X}..0x{hi:X}");
    foreach (var (at, value) in hits) Console.WriteLine($"  at 0x{at:X} -> 0x{value:X}  (offset +0x{value - lo:X} from lo)");
    return 0;
}

// findvt <address> [maxBackHex]   walk backwards for the nearest qword pointing into game_plugin (an object vtable)
int FindVtCommand(string[] rest)
{
    if (rest.Length < 1) { Console.Error.WriteLine("usage: findvt <address> [maxBackHex]"); return 2; }
    ulong address = Convert.ToUInt64(rest[0].Replace("0x", ""), 16);
    ulong maxBack = rest.Length > 1 ? Convert.ToUInt64(rest[1].Replace("0x", ""), 16) : 0x4000;
    using var session = Attach(writable: false);
    var mem = session.Memory;
    ulong mb = session.Game.ModuleBase, me = session.Game.ModuleEnd;
    Console.WriteLine($"from 0x{address:X} back 0x{maxBack:X} (module 0x{mb:X}..0x{me:X})");
    for (ulong step = 8; step <= maxBack; step += 8)
    {
        ulong at = address - step;
        Span<byte> b = stackalloc byte[8];
        if (!mem.TryRead(at, b)) continue;
        ulong v = BitConverter.ToUInt64(b);
        if (v < mb || v >= me) continue;
        Console.WriteLine($"  vtable 0x{v:X} (+0x{v - mb:X}) at 0x{at:X}  (block starts 0x{at + 8:X}, distance -0x{step:X})");
        return 0;
    }
    Console.WriteLine("  no module pointer found");
    return 0;
}

// vtscan <rva-decimal-or-0xhex> [maxHits]   find objects whose vtable equals module+rva in writable memory
int VtScanCommand(string[] rest)
{
    if (rest.Length < 1) { Console.Error.WriteLine("usage: vtscan <rva> [maxHits]"); return 2; }
    ulong rva = rest[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        ? Convert.ToUInt64(rest[0][2..], 16)
        : ulong.Parse(rest[0]);
    int maxHits = rest.Length > 1 ? int.Parse(rest[1]) : 10;
    using var session = Attach(writable: false);
    var memory = session.Memory;
    if (memory is not Fm27Editor.Core.Memory.WindowsProcessMemory wpm)
    { Console.Error.WriteLine("region scan needs the Windows process memory"); return 2; }
    ulong wanted = session.Game.ModuleBase + rva;
    Console.WriteLine($"scanning writable regions for vtable 0x{wanted:X} (module+0x{rva:X})");
    int hits = 0;
    const int chunk = 1 << 22;
    var buffer = new byte[chunk];
    foreach (var region in wpm.EnumerateWritableRegions())
    {
        if (region.IsModuleData) continue;
        for (ulong offset = 0; offset + 8 <= region.Size; offset += (ulong)chunk)
        {
            int length = (int)Math.Min((ulong)chunk, region.Size - offset);
            if (!memory.TryRead(region.Start + offset, buffer.AsSpan(0, length))) break;
            for (int i = 0; i + 8 <= length; i += 8)
            {
                if (BitConverter.ToUInt64(buffer, i) != wanted) continue;
                Console.WriteLine($"  object 0x{region.Start + offset + (ulong)i + 8:X}");
                if (++hits >= maxHits) return 0;
            }
        }
    }
    Console.WriteLine($"hits {hits}");
    return 0;
}

// dealscan <club-id>   look for a pointer whose target carries the transfer-deal vtable (74,494,248 / 83,071,720)
int DealScanCommand(int clubId)
{
    using var session = Attach(writable: false);
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var memory = session.Memory;
    ulong module = session.Game.ModuleBase;
    ulong[] dealVtables = [module + 74494248, module + 83071720];
    ulong Q(ulong a)
    {
        Span<byte> b = stackalloc byte[8];
        return memory.TryRead(a, b) ? BitConverter.ToUInt64(b) : 0;
    }
    bool IsDeal(ulong address)
    {
        Span<byte> b = stackalloc byte[8];
        if (address < 0x10000 || !memory.TryRead(address, b)) return false;
        ulong vtable = BitConverter.ToUInt64(b);
        return vtable == dealVtables[0] || vtable == dealVtables[1];
    }
    void Scan(string label, ulong baseAddress)
    {
        for (int off = 0; off <= 0x200; off += 8)
        {
            ulong ptr = Q(baseAddress + (ulong)off);
            if (IsDeal(ptr))
                Console.WriteLine($"  {label} +0x{off:X} -> deal 0x{ptr:X}");
        }
    }
    foreach (var team in club.Teams) Scan($"team 0x{team.Address:X}", team.Address);
    Scan($"club 0x{club.Address:X}", club.Address);
    return 0;
}

// dealsprobe <club-id>   walk the club's transfer-deal pointer at club+0x18 and print the first entries
int DealsProbeCommand(int clubId)
{
    using var session = Attach(writable: false);
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var memory = session.Memory;
    ulong module = session.Game.ModuleBase;
    ulong Q(ulong a)
    {
        Span<byte> b = stackalloc byte[8];
        return memory.TryRead(a, b) ? BitConverter.ToUInt64(b) : 0;
    }
    ulong list = Q(club.Address + 0x18);
    Console.WriteLine($"{club.Name}: club 0x{club.Address:X} +0x18 -> 0x{list:X}");
    if (list == 0) return 0;
    for (int i = 0; i < 4; i++)
    {
        Span<byte> b = stackalloc byte[8];
        if (!memory.TryRead(list + (ulong)(i * 8), b)) break;
        Console.WriteLine($"  list+0x{i * 8:X} = 0x{BitConverter.ToUInt64(b):X}");
    }
    ulong deal = Q(list);
    if (deal == 0) return 0;
    var head = new byte[8];
    if (memory.TryRead(deal, head))
        Console.WriteLine($"  first pointer 0x{deal:X} vtable 0x{BitConverter.ToUInt64(head) - module:X}");
    var fee = memory.Read(deal + 88, 4);
    var status = memory.Read(deal + 136, 4);
    Console.WriteLine($"  fee@88 {BitConverter.ToInt32(fee, 0):N0} status@136 0x{BitConverter.ToInt32(status, 0):X} ({BitConverter.ToInt32(status, 0)})");
    return 0;
}

// msccfind [loHex hiHex]   try every staff row as the human base: c = Q(base+0x80), mscc = Q(c+0x370), print hits in [lo,hi)
int MsccFindCommand(string[] rest)
{
    ulong lo = rest.Length > 0 ? Convert.ToUInt64(rest[0].Replace("0x", ""), 16) : 0x10000;
    ulong hi = rest.Length > 1 ? Convert.ToUInt64(rest[1].Replace("0x", ""), 16) : 0x7FFFFFFFFFFF;
    using var session = Attach(writable: false);
    var memory = session.Memory;
    ulong Q(ulong a)
    {
        Span<byte> b = stackalloc byte[8];
        return memory.TryRead(a, b) ? BitConverter.ToUInt64(b) : 0;
    }
    var staff = session.Staff.LoadAll();
    Console.WriteLine($"{staff.Count} staff rows; looking for mscc in 0x{lo:X}..0x{hi:X}");
    int hits = 0;
    foreach (var s in staff)
    {
        foreach (var (label, baseAddress) in new[] { ("staff", s.StaffAddress), ("person", s.Address) })
        {
            if (baseAddress < 0x10000) continue;
            ulong c = Q(baseAddress + 0x80);
            if (c < 0x10000) continue;
            ulong mscc = Q(c + 0x370);
            if (mscc < lo || mscc >= hi) continue;
            Console.WriteLine($"  {s.DisplayName,-28} id {s.Id,-9} {label} 0x{baseAddress:X} c 0x{c:X} mscc 0x{mscc:X}");
            foreach (int offset in new[] { 4586, 4587, 4588, 4589, 4590, 4591, 4595, 4596, 4597, 4598, 4599, 4600, 4605, 4606 })
            {
                Span<byte> one = stackalloc byte[1];
                if (memory.TryRead(mscc + (ulong)offset, one)) Console.Write($" {one[0]}");
            }
            Console.WriteLine();
            hits++;
        }
    }
    Console.WriteLine($"hits {hits}");
    return 0;
}

// msccscan <club-id>   find a pointer in the club whose target carries small allowance bytes at +4586..+4606
int MsccScanCommand(int clubId)
{
    int[] offsets = [4586, 4587, 4588, 4589, 4590, 4591, 4595, 4596, 4597, 4598, 4599, 4600, 4605, 4606];
    using var session = Attach(writable: false);
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var memory = session.Memory;
    ulong Q(ulong a)
    {
        Span<byte> buffer = stackalloc byte[8];
        return memory.TryRead(a, buffer) ? BitConverter.ToUInt64(buffer) : 0;
    }
    void Test(string label, ulong candidate)
    {
        if (candidate < 0x10000) return;
        var bytes = new byte[offsets.Length];
        for (int i = 0; i < offsets.Length; i++)
        {
            Span<byte> one = stackalloc byte[1];
            if (!memory.TryRead(candidate + (ulong)offsets[i], one)) return;
            bytes[i] = one[0];
        }
        if (bytes.All(b => b == 0) || bytes.Any(b => b > 60)) return;
        Console.WriteLine($"  {label}: mscc 0x{candidate:X} allowances {string.Join(",", bytes)}");
    }
    for (int off = 0; off <= 0x800; off += 8)
    {
        ulong target = Q(club.Address + (ulong)off);
        Test($"club+0x{off:X}", target);
    }
    // one more hop through likely sub-objects
    for (int off = 0; off <= 0x800; off += 8)
    {
        ulong first = Q(club.Address + (ulong)off);
        if (first < 0x10000) continue;
        for (int inner = 0; inner <= 0x800; inner += 8)
        {
            ulong second = Q(first + (ulong)inner);
            Test($"club+0x{off:X}->0x{inner:X}", second);
        }
    }
    return 0;
}

// staffallow <club-id> [set <role> <value> --write]   FM Studio staff allowances: club+880 -> bytes at +4586..4606
int StaffAllowCommand(int clubId, string[] rest)
{
    // Constants from FM26Studio.Services.StaffAllowanceService (v1.1.1 const fields).
    (string Role, int Offset)[] roles =
    [
        ("Coach Senior", 4586), ("Coach U21", 4587), ("Coach U18", 4588),
        ("Physio Senior", 4589), ("Physio U21", 4590), ("Physio U18", 4591),
        ("Sports Scientist Senior", 4595), ("Sports Scientist U21", 4596), ("Sports Scientist U18", 4597),
        ("Performance Analyst Senior", 4598), ("Performance Analyst U21", 4599), ("Performance Analyst U18", 4600),
        ("Scouts", 4605), ("Recruitment Analyst", 4606),
    ];
    using var session = Attach(writable: rest.Contains("--write"));
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var memory = session.Memory;
    ulong Q(ulong a)
    {
        Span<byte> buffer = stackalloc byte[8];
        return memory.TryRead(a, buffer) ? BitConverter.ToUInt64(buffer) : 0;
    }
    // FM Studio GetMsccAddress: c = ReadU64(obj + 128); mscc = ReadU64(c + 880).
    ulong step = Q(club.Address + 128);
    ulong holder = step == 0 ? 0 : Q(step + 880);
    Console.WriteLine($"{club.Name}: club 0x{club.Address:X} +128 -> 0x{step:X} +880 -> 0x{holder:X}");
    if (holder == 0) return 0;
    foreach (var (role, offset) in roles)
    {
        var raw = memory.Read(holder + (ulong)offset, 1);
        Console.WriteLine($"  {role,-28} +{offset}  {(raw.Length == 1 ? raw[0] : (byte)0)}");
    }
    // The human path: FM Studio passes the manager's human pointer as the second argument.
    ulong explicitHuman = rest.SkipWhile(w => !w.Equals("--human", StringComparison.OrdinalIgnoreCase)).Skip(1)
        .Select(w => w.StartsWith("0x") ? Convert.ToUInt64(w[2..], 16) : 0UL).FirstOrDefault();
    var managers = explicitHuman != 0 ? [] : session.Staff.LoadAll().Where(x => x.JobCode == 16).ToArray();
    var near = managers.Where(x => x.Club is not null
        && (x.Club.DisplayName.Contains("Man", StringComparison.OrdinalIgnoreCase)
            || x.Club.DisplayName.Contains(club.Name, StringComparison.OrdinalIgnoreCase))).Take(6).ToArray();
    foreach (var candidate in near)
        Console.WriteLine($"  manager candidate: {candidate.DisplayName} club '{candidate.Club?.DisplayName}' 0x{candidate.Address:X}");
    var manager = explicitHuman != 0
        ? session.Staff.LoadAll().FirstOrDefault(x => x.Address == explicitHuman)
        : near.FirstOrDefault();
    if (manager is not null)
    {
        ulong p1 = Q(manager.Address + 0x18);
        ulong p2 = p1 == 0 ? 0 : Q(p1);
        ulong obj = p2 >= 0x10000 && p2 <= 0x7FFFFFFFFFFF ? p2 : p1;
        ulong c = obj == 0 ? 0 : Q(obj + 0x80);
        ulong mscc = c == 0 ? 0 : Q(c + 0x370);
        Console.WriteLine($"manager {manager.DisplayName} 0x{manager.Address:X}: obj 0x{obj:X} c 0x{c:X} mscc 0x{mscc:X}");
        if (mscc != 0)
            foreach (var (role, offset) in roles)
            {
                Span<byte> one = stackalloc byte[1];
                if (session.Memory.TryRead(mscc + (ulong)offset, one))
                    Console.WriteLine($"  [human] {role,-28} +{offset}  {one[0]}");
            }
    }
    else Console.WriteLine("no manager staff row found for this club");

    var words = rest.Where(w => w != "--write" && w != "--human" && !w.StartsWith("0x", StringComparison.Ordinal)).ToArray();
    if (words.Length < 3 || !words[0].Equals("set", StringComparison.OrdinalIgnoreCase)) return 0;
    var match = roles.FirstOrDefault(r => r.Role.Contains(words[1], StringComparison.OrdinalIgnoreCase));
    if (match.Role is null) { Console.Error.WriteLine($"unknown role {words[1]}"); return 2; }
    byte value = byte.Parse(words[2]);
    ulong target = holder + (ulong)match.Offset;
    Console.WriteLine($"set {match.Role} at 0x{target:X} -> {value}" + (rest.Contains("--write") ? "" : " (plan only)"));
    if (!rest.Contains("--write")) return 0;
    if (!memory.TryWrite(target, [value]) || memory.Read(target, 1)[0] != value)
    { Console.Error.WriteLine("write failed"); return 1; }
    Console.WriteLine($"read back {memory.Read(target, 1)[0]}");
    return 0;
}

// regprobe <player-name> [<player-name> ...]   walk player+0x288 -> +0x130 to look for the domestic-registration flag
int RegProbeCommand(string[] names)
{
    using var session = Attach(writable: false);
    var players = session.Players.LoadAll();
    foreach (var name in names)
    {
        var matches = players.Where(p => p.DisplayName.Contains(name, StringComparison.OrdinalIgnoreCase)).Take(3).ToArray();
        if (matches.Length == 0) { Console.WriteLine($"{name}: not found"); continue; }
        foreach (var player in matches)
        {
            var head = session.Memory.Read(player.Address, 8);
            ulong vtable = head.Length < 8 ? 0 : BitConverter.ToUInt64(head, 0);
            var at288 = session.Memory.Read(player.Address + 0x288, 8);
            ulong holder = at288.Length < 8 ? 0 : BitConverter.ToUInt64(at288, 0);
            string chain = holder == 0 ? "no holder" : $"holder 0x{holder:X}";
            var word = holder == 0 ? [] : session.Memory.Read(holder + 0x130, 4);
            string flags = word.Length < 4 ? "-" : Convert.ToHexString(word);
            var body = holder == 0 ? [] : session.Memory.Read(holder + 0x30, 8);
            string bodyHex = body.Length < 8 ? "-" : Convert.ToHexString(body);
            Console.WriteLine($"{player.DisplayName} ({player.Id}) addr 0x{player.Address:X} vtable 0x{vtable - session.Game.ModuleBase:X} " +
                              $"+0x288 {chain} +0x130 {flags} +0x30 {bodyHex}");
        }
    }
    return 0;
}

// visionadd <club-id> <template-row> <type> <importance> <target> <start> <end> [--write]
int VisionAddCommand(int clubId, string[] rest)
{
    bool write = rest.Contains("--write");
    var w = rest.Where(a => a != "--write").ToArray();
    if (w.Length < 6) { Console.Error.WriteLine("usage: visionadd <clubId> <templateRow> <type> <importance> <target> <start> <end> [--write]"); return 2; }
    int templateIndex = int.Parse(w[0]);
    byte type = byte.Parse(w[1]);
    byte importance = byte.Parse(w[2]);
    int target = int.Parse(w[3]);
    var start = DateOnly.ParseExact(w[4], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    var end = DateOnly.ParseExact(w[5], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    using var session = Attach(writable: write);
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var team = club.Teams[0];
    var rows = session.ReadClubVision(team.Address, null);
    if (templateIndex < 0 || templateIndex >= rows.Count) { Console.Error.WriteLine($"club has {rows.Count} rows"); return 2; }
    Console.WriteLine($"{club.Name}: {rows.Count} rows; template {templateIndex} type {rows[templateIndex].TypeCode}");
    Console.WriteLine($"new row type {type} importance {importance} target {target} {start:yyyy-MM-dd}..{end:yyyy-MM-dd}" + (write ? "" : " (plan only)"));
    if (!write) return 0;
    var allocate = session.Memory as Fm27Editor.Core.Memory.WindowsProcessMemory;
    var added = session.ClubVisionWriter.Create(team.Address, rows[templateIndex], type, importance, target, start, end,
        allocate is null ? null : allocate.Allocate);
    Console.WriteLine($"added at 0x{added.Address:X}; now {session.ReadClubVision(team.Address, null).Count} rows");
    return 0;
}

// visionobj <club-id> <row>   hexdump the two objects a culture record points at (+0x40, +0x48)
int VisionObjCommand(int clubId, int rowIndex)
{
    using var session = Attach(writable: false);
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var rows = session.ReadClubVision(club.Teams[0].Address, null);
    if (rowIndex < 0 || rowIndex >= rows.Count) { Console.Error.WriteLine($"row {rowIndex} out of range"); return 2; }
    var row = rows[rowIndex];
    var raw = session.Memory.Read(row.Address, 0x50);
    Console.WriteLine($"{club.Name} row {rowIndex}: type {row.TypeCode} importance {row.Importance} {row.StartDate:yyyy-MM-dd}..{row.EndDate:yyyy-MM-dd}");
    for (int offset = 0x40; offset <= 0x48; offset += 8)
    {
        ulong target = BitConverter.ToUInt64(raw, offset);
        Console.WriteLine($"  +0x{offset:X} -> 0x{target:X}");
        var body = session.Memory.Read(target, 0x80);
        for (int i = 0; i < body.Length; i += 16)
            Console.WriteLine($"    {i:X4}  {Convert.ToHexString(body.AsSpan(i, Math.Min(16, body.Length - i)))}");
    }
    return 0;
}

// visiondump <club-id> [count]   raw 0x50 bytes of the first culture records, for field hunting
int VisionDumpCommand(int clubId, int count)
{
    using var session = Attach(writable: false);
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var rows = session.ReadClubVision(club.Teams[0].Address, null);
    Console.WriteLine($"{club.Name}: {rows.Count} rows");
    foreach (var row in rows.Take(count))
    {
        var raw = session.Memory.Read(row.Address, 0x50);
        Console.WriteLine($"  0x{row.Address:X} type {row.TypeCode} importance {row.Importance} {row.StartDate:yyyy-MM-dd}..{row.EndDate:yyyy-MM-dd}");
        Console.WriteLine($"    {Convert.ToHexString(raw)}");
        Console.WriteLine($"    i32@20 {BitConverter.ToInt32(raw, 0x20)} i32@24 {BitConverter.ToInt32(raw, 0x24)} " +
                          $"i32@28 {BitConverter.ToInt32(raw, 0x28)} b@2C {raw[0x2C]} b@2D {raw[0x2D]} " +
                          $"i32@30 {BitConverter.ToInt32(raw, 0x30)} i32@34 {BitConverter.ToInt32(raw, 0x34)} " +
                          $"b@38 {raw[0x38]} b@39 {raw[0x39]} i32@3C {BitConverter.ToInt32(raw, 0x3C)}");
        Console.WriteLine($"    bytes 30-47 {Convert.ToHexString(raw.AsSpan(0x30, 0x18))}  " +
                          $"u16@3A {BitConverter.ToUInt16(raw, 0x3A)} u16@3E {BitConverter.ToUInt16(raw, 0x3E)} " +
                          $"u16@40 {BitConverter.ToUInt16(raw, 0x40)} u16@44 {BitConverter.ToUInt16(raw, 0x44)}");
    }
    return 0;
}

// stadiumprobe   walk the global stadium list pointer and say where it stops
int StadiumProbeCommand()
{
    using var session = Attach(writable: false);
    var memory = session.Memory;
    var s = session.Profile.Stadium;
    if (s is null) { Console.Error.WriteLine("profile has no stadium offsets"); return 2; }
    ulong module = session.Game.ModuleBase;
    ulong rootAddress = module + s.ListRootRva;
    var rootRaw = memory.Read(rootAddress, 8);
    ulong root = rootRaw.Length < 8 ? 0 : BitConverter.ToUInt64(rootRaw, 0);
    Console.WriteLine($"module 0x{module:X} listRootRva 0x{s.ListRootRva:X} -> rootAddress 0x{rootAddress:X} root 0x{root:X}");
    if (root == 0) return 0;
    var vec = memory.Read(root + (ulong)s.ListOffset, 0x18);
    if (vec.Length < 0x18) { Console.WriteLine("vector unreadable"); return 0; }
    ulong begin = BitConverter.ToUInt64(vec, 0), end = BitConverter.ToUInt64(vec, 8), cap = BitConverter.ToUInt64(vec, 16);
    Console.WriteLine($"  listOffset 0x{s.ListOffset:X}: begin 0x{begin:X} end 0x{end:X} cap 0x{cap:X}" +
                      (end >= begin && (end - begin) % 8 == 0 ? $" count {(end - begin) / 8:N0}" : " (bad)"));
    // Nearby qwords from the root: helps spot a moved list offset without a full profile rebuild.
    var window = memory.Read(root, 0x2000);
    for (int off = 0; off + 16 <= window.Length; off += 8)
    {
        ulong b = BitConverter.ToUInt64(window, off), e = BitConverter.ToUInt64(window, off + 8);
        if (b == 0 || e <= b || (e - b) % 8 != 0) continue;
        long count = (long)((e - b) / 8);
        if (count is < 100 or > 200000) continue;
        var firstPtr = memory.Read(b, 8);
        ulong first = firstPtr.Length < 8 ? 0 : BitConverter.ToUInt64(firstPtr, 0);
        var head = first == 0 ? [] : memory.Read(first, 8);
        ulong vtable = head.Length < 8 ? 0 : BitConverter.ToUInt64(head, 0);
        uint vtableRva = (uint)(vtable - module);
        string marker = vtableRva == s.VtableRva ? "  <== STADIUM" : "";
        Console.WriteLine($"  root+0x{off:X}: count {count:N0} vtable 0x{vtableRva:X}{marker}");
    }
    return 0;
}

// visionprobe <club-id>   walk the club team's vision pointers and say where the read stops
int VisionProbeCommand(int clubId)
{
    using var session = Attach(writable: false);
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var memory = session.Memory;
    var layout = session.Profile.Club?.Lists;
    if (layout is null) { Console.Error.WriteLine("profile has no club lists"); return 2; }
    ulong Q(ulong a)
    {
        var b = memory.Read(a, 8);
        return b.Length < 8 ? 0 : BitConverter.ToUInt64(b, 0);
    }
    Console.WriteLine($"{club.Name}: {club.Teams.Count} teams");
    foreach (var team in club.Teams)
    {
        var head = memory.Read(team.Address, 8);
        ulong vtable = head.Length < 8 ? 0 : BitConverter.ToUInt64(head, 0);
        ulong detail = Q(team.Address + (ulong)layout.TeamDetails);
        ulong holder = detail == 0 ? 0 : Q(detail + (ulong)layout.Vision);
        ulong header = holder == 0 ? 0 : Q(holder + (ulong)layout.VisionVector);
        var holderHead = holder == 0 ? [] : memory.Read(holder, 8);
        ulong holderVtable = holderHead.Length < 8 ? 0 : BitConverter.ToUInt64(holderHead, 0);
        var vec = header == 0 ? [] : memory.Read(header, 24);
        string vectorText = vec.Length < 24 ? "-"
            : $"begin 0x{BitConverter.ToUInt64(vec, 0):X} end 0x{BitConverter.ToUInt64(vec, 8):X} cap 0x{BitConverter.ToUInt64(vec, 16):X}";
        Console.WriteLine($"  team 0x{team.Address:X} vtable 0x{vtable - session.Game.ModuleBase:X} " +
                          $"detail 0x{detail:X} holder 0x{holder:X} holderVtable 0x{holderVtable - session.Game.ModuleBase:X} vector {vectorText}");
    }
    // First few culture records: their vtable is the reliable anchor when team/holder vtables move between builds.
    var firstTeam = club.Teams[0];
    ulong firstDetail = Q(firstTeam.Address + (ulong)layout.TeamDetails);
    ulong firstHolder = firstDetail == 0 ? 0 : Q(firstDetail + (ulong)layout.Vision);
    ulong firstHeader = firstHolder == 0 ? 0 : Q(firstHolder + (ulong)layout.VisionVector);
    var firstVec = firstHeader == 0 ? [] : memory.Read(firstHeader, 24);
    if (firstVec.Length == 24)
    {
        ulong begin = BitConverter.ToUInt64(firstVec, 0), end = BitConverter.ToUInt64(firstVec, 8);
        int count = (int)Math.Min(3, (long)((end - begin) / 8));
        for (int i = 0; i < count; i++)
        {
            ulong record = Q(begin + (ulong)(i * 8));
            var head = record == 0 ? [] : memory.Read(record, 8);
            ulong vtable = head.Length < 8 ? 0 : BitConverter.ToUInt64(head, 0);
            var body = record == 0 ? [] : memory.Read(record, 0x30);
            Console.WriteLine($"    record {i} 0x{record:X} vtable 0x{vtable - session.Game.ModuleBase:X}" +
                              (body.Length >= 0x30 ? $" type {body[layout.VisionType]} importance {body[layout.Importance]}" : ""));
        }
    }
    var rows = session.ReadClubVision(club.Teams[0].Address);
    Console.WriteLine($"reader returned {rows.Count} rows for team 0x{club.Teams[0].Address:X}");
    return 0;
}

// clubdebt <club-id> clear [--write]   drops every debt of the club
int ClubDebtCommand(string[] args)
{
    bool write = args.Contains("--write");
    var words = args.Where(a => a != "--write").ToArray();
    if (words.Length < 2 || !words[1].Equals("clear", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("usage: clubdebt <club-id> clear [--write]");
        return 2;
    }
    int clubId = int.Parse(words[0]);
    using var session = Attach(writable: write);
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var debts = session.ReadDebts(club.Address);
    Console.WriteLine($"{club.Name}: {debts.Count} debts");
    foreach (var d in debts)
        Console.WriteLine($"  source {d.SourceCode,3} {d.OriginalDebt,14:N0} {d.StartDate:yyyy-MM-dd}..{d.EndDate:yyyy-MM-dd}");
    if (!write) { Console.WriteLine("plan only; add --write to clear"); return 0; }
    session.RemoveAllDebts(club.Address);
    Console.WriteLine($"cleared; {session.ReadDebts(club.Address).Count} debts after");
    return 0;
}

// clubsponsor add <club-id> <income-type> <total> <start,end> [--write]   appends one sponsor record
int ClubSponsorAdd(string clubText, string[] rest, bool write)
{
    if (rest.Length < 3) { Console.Error.WriteLine("usage: clubsponsor <club-id> add <income-type> <total> <start,end> [--write]"); return 2; }
    int clubId = int.Parse(clubText);
    byte type = byte.Parse(rest[0]);
    int total = int.Parse(rest[1]);
    var dates = rest[2].Split(',');
    if (dates.Length != 2) { Console.Error.WriteLine("dates take start,end as yyyy-MM-dd"); return 2; }
    var start = DateOnly.ParseExact(dates[0], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    var end = DateOnly.ParseExact(dates[1], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    using var session = Attach(writable: write);
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    Console.WriteLine($"{club.Name}: {session.ReadSponsors(club.Address).Count} sponsors before");
    Console.WriteLine($"add type {type} total {total:N0} {start:yyyy-MM-dd}..{end:yyyy-MM-dd}" + (write ? "" : " (plan only; add --write)"));
    if (!write) return 0;
    var added = session.SponsorWriter.Add(club.Address, type, total, start, end);
    Console.WriteLine($"added at 0x{added.Address:X}; {session.ReadSponsors(club.Address).Count} sponsors after");
    return 0;
}

// clubsponsor remove <club-id> <index> [--write]   drops one entry from the sponsor list
int ClubSponsorRemove(string clubText, string indexText, bool write)
{
    int clubId = int.Parse(clubText), index = int.Parse(indexText);
    using var session = Attach(writable: write);
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var sponsors = session.ReadSponsors(club.Address);
    if (index < 0 || index >= sponsors.Count) { Console.Error.WriteLine($"club has {sponsors.Count} sponsors"); return 2; }
    var entry = sponsors[index];
    Console.WriteLine($"remove {club.Name} sponsor {index}: type {entry.IncomeTypeCode} total {entry.TotalValue:N0}" + (write ? "" : " (plan only; add --write)"));
    if (!write) return 0;
    session.SponsorWriter.Remove(club.Address, entry);
    Console.WriteLine($"removed; {session.ReadSponsors(club.Address).Count} sponsors after");
    return 0;
}

// clubfields <id>   every club.fields mapping read from the live game (club fields + first team)
int ClubListsCommand(int clubId)
{
    using var session = Attach(writable: false);
    var clubs = session.LoadClubs(session.Players.LoadAll());
    var club = clubs.SingleOrDefault(c => c.Id == clubId) ?? session.ScanAllClubs().SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var vision = session.ReadClubVision(club.FirstTeam?.Address ?? 0, clubs);
    var affiliations = session.ReadAffiliations(club.Address);
    var regional = session.ReadRegionalDivisions(club.Address);
    Console.WriteLine($"{club.Name} id {clubId}: confirmed culture {vision.Count}, affiliations {affiliations.Count}, regional {regional.Count}");
    foreach (var r in vision)
        Console.WriteLine($"  vision type {r.TypeCode} importance {r.Importance} start {r.StartDate:yyyy-MM-dd} end {r.EndDate:yyyy-MM-dd} item {r.ItemIndex} {r.ClubItem?.DisplayName}");
    foreach (var r in affiliations)
        Console.WriteLine($"  affiliation {r.ParentClub?.DisplayName} -> {r.FeederClub?.DisplayName} type {r.TypeCode} start {r.StartDate:yyyy-MM-dd}");
    foreach (var r in regional) Console.WriteLine($"  regional level {r.Level}");
    return 0;
}

int ClubVisionCommand(string[] args)
{
    bool write = args.Contains("--write");
    int fromIndex = Array.IndexOf(args, "--from");
    int? from = fromIndex >= 0 ? int.Parse(args[fromIndex + 1]) : null;
    var words = args.Where((_, i) => args[i] != "--write" && i != fromIndex && (fromIndex < 0 || i != fromIndex + 1)).ToArray();
    if (words.Length < 3) throw new ArgumentException("usage: clubvision <club-id> <type-code> <importance> [--from <current importance>] [--write]");
    int clubId = int.Parse(words[0]), type = int.Parse(words[1]), importance = int.Parse(words[2]);
    using var session = Attach(writable: write);
    var clubs = session.LoadClubs(session.Players.LoadAll());
    var club = clubs.SingleOrDefault(c => c.Id == clubId) ?? session.ScanAllClubs().SingleOrDefault(c => c.Id == clubId);
    var team = club?.FirstTeam ?? throw new InvalidOperationException($"No first team for club {clubId}");
    var matches = session.ReadClubVision(team.Address, clubs).Where(r => r.TypeCode == type).ToList();
    if (from is { } expected) matches = matches.Where(r => r.Importance == expected).ToList();
    if (matches.Count != 1)
        throw new InvalidOperationException($"Club {clubId} has {matches.Count} vision rows of type {type}" +
            (from is null ? "; add --from <current importance>" : ""));
    var entry = matches[0];
    Console.WriteLine($"{club!.Name} id {clubId} team 0x{team.Address:X}: type {entry.TypeCode} importance {entry.Importance} -> {importance}");
    if (!write) { Console.WriteLine("plan only; add --write to apply"); return 0; }
    session.SetClubVisionImportance(team.Address, entry, importance);
    var after = session.ReadClubVision(team.Address, clubs).Single(r => r.Address == entry.Address);
    if (after.Importance != importance) throw new StaleObjectException("Club vision readback failed");
    Console.WriteLine($"applied; type {after.TypeCode} importance {after.Importance}");
    return 0;
}

// clubdeep <idA> <idB> <size> <depth> <span> <a>:<b>...   RE (read only): walk both clubs' pointer graphs in lockstep (same slot offsets),
// report every offset inside the first <span> bytes of a node pair where the integers equal a (club A) and b (club B).
int ClubDeepCommand(int idA, int idB, int size, int depth, int span, string[] pairs)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var clubs = session.LoadClubs(session.Players.LoadAll());
    var a = clubs.First(c => c.Id == idA); var b = clubs.First(c => c.Id == idB);
    var want = pairs.Select(s => s.Split(':')).Select(x => (A: long.Parse(x[0]), B: long.Parse(x[1]))).ToList();
    long V(byte[] d, int o) => size switch { 1 => d[o], 2 => BitConverter.ToInt16(d, o), 4 => BitConverter.ToInt32(d, o), _ => 0 };
    var seen = new HashSet<ulong>();
    var queue = new Queue<(ulong A, ulong B, string Path, int Depth)>();
    queue.Enqueue((a.Address, b.Address, "club", 0));
    int nodes = 0, hits = 0;
    while (queue.Count > 0 && nodes < 300000 && hits < 200)
    {
        var (pa, pb, path, d) = queue.Dequeue();
        var da = new byte[span]; var db = new byte[span];
        if (!mem.TryRead(pa, da) || !mem.TryRead(pb, db)) continue;
        nodes++;
        for (int o = 0; o + size <= span; o++)
            foreach (var (x, y) in want)
                if (V(da, o) == x && V(db, o) == y && BitConverter.ToUInt64(da) - session.Game.ModuleBase < 0x8000000UL && hits++ < 200) Console.WriteLine($"  {path}+0x{o:X}  A=0x{pa:X} B=0x{pb:X}  vt +0x{BitConverter.ToUInt64(da) - session.Game.ModuleBase:X}");
        if (d >= depth) continue;
        for (int k = 0; k + 8 <= span; k += 8)
        {
            ulong ca = BitConverter.ToUInt64(da, k), cb = BitConverter.ToUInt64(db, k);
            if (ca < 0x10000000000 || cb < 0x10000000000 || ca > 0x7FF000000000 || cb > 0x7FF000000000 || ca == cb || !seen.Add(ca)) continue;
            queue.Enqueue((ca, cb, $"{path}[+0x{k:X}]", d + 1));
        }
    }
    Console.WriteLine($"{nodes} node pairs, {hits} hits");
    return 0;
}

// nationdeep <nationA> <nationB> <size> <depth> <span> <a>:<b>...   RE (read only): like clubdeep, starting from two nation objects.
int NationDeepCommand(string nameA, string nameB, int size, int depth, int span, string[] pairs)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var nations = session.Nations.LoadAll();
    var a = nations.First(n => string.Equals(n.Name, nameA, StringComparison.OrdinalIgnoreCase));
    var b = nations.First(n => string.Equals(n.Name, nameB, StringComparison.OrdinalIgnoreCase));
    var want = pairs.Select(s => s.Split(':')).Select(x => (A: long.Parse(x[0]), B: long.Parse(x[1]))).ToList();
    long V(byte[] d, int o) => size switch { 1 => d[o], 2 => BitConverter.ToInt16(d, o), 4 => BitConverter.ToInt32(d, o), _ => 0 };
    var seen = new HashSet<ulong>();
    var queue = new Queue<(ulong A, ulong B, string Path, int Depth)>();
    queue.Enqueue((a.Address, b.Address, "nation", 0));
    int nodes = 0, hits = 0;
    while (queue.Count > 0 && nodes < 300000 && hits < 100)
    {
        var (pa, pb, path, d) = queue.Dequeue();
        var da = new byte[span]; var db = new byte[span];
        if (!mem.TryRead(pa, da) || !mem.TryRead(pb, db)) continue;
        nodes++;
        for (int o = 0; o + size <= span; o++)
            foreach (var (x, y) in want)
                if (V(da, o) == x && V(db, o) == y && BitConverter.ToUInt64(da) - session.Game.ModuleBase < 0x8000000UL && hits++ < 100)
                    Console.WriteLine($"  {path}+0x{o:X}  A=0x{pa:X} B=0x{pb:X}  vt +0x{BitConverter.ToUInt64(da) - session.Game.ModuleBase:X}");
        if (d >= depth) continue;
        for (int k = 0; k + 8 <= span; k += 8)
        {
            ulong ca = BitConverter.ToUInt64(da, k), cb = BitConverter.ToUInt64(db, k);
            if (ca < 0x10000000000 || cb < 0x10000000000 || ca > 0x7FF000000000 || cb > 0x7FF000000000 || ca == cb || !seen.Add(ca)) continue;
            queue.Enqueue((ca, cb, $"{path}[+0x{k:X}]", d + 1));
        }
    }
    Console.WriteLine($"{nodes} node pairs, {hits} hits");
    return 0;
}


// nationfind <nation> <depth> <target nation>...: RE (read only), walks the pointer graph from the nation (first 0x400 bytes of each
// node) and prints where an 8-byte value equals one of the target nations' addresses.
int NationFindCommand(string name, int depth, string[] targets)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var nations = session.Nations.LoadAll();
    var start = nations.First(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));
    var want = targets.ToDictionary(t => nations.First(n => string.Equals(n.Name, t, StringComparison.OrdinalIgnoreCase)).Address, t => t);
    var seen = new HashSet<ulong> { start.Address };
    var queue = new Queue<(ulong Addr, string Path, int Depth)>();
    queue.Enqueue((start.Address, "nation", 0));
    int hits = 0, nodes = 0;
    while (queue.Count > 0 && hits < 60 && nodes < 100000)
    {
        var (addr, path, d) = queue.Dequeue();
        var b = new byte[0x400];
        if (!mem.TryRead(addr, b)) continue;
        nodes++;
        for (int k = 0; k + 8 <= b.Length; k += 8)
        {
            ulong p = BitConverter.ToUInt64(b, k);
            if (want.TryGetValue(p, out var t)) { Console.WriteLine($"  {path}+0x{k:X} (node 0x{addr:X}) -> {t}"); hits++; }
            if (d < depth && p is > 0x10000000000 and < 0x7FF000000000 && seen.Add(p)) queue.Enqueue((p, $"{path}[+0x{k:X}]", d + 1));
        }
    }
    Console.WriteLine($"{nodes} nodes");
    return 0;
}

// nationteams <name>: read only, the men's national teams of the matching nations with their reputation.
int NationTeamsCommand(string query)
{
    using var session = Attach(writable: false);
    foreach (var n in session.Nations.LoadAll().Where(n => query.Length == 0 || (n.Name ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)).Take(20))
        Console.WriteLine($"{n.Name,-24} " + string.Join(" ", session.NationTeams.Read(n.Address).Select(t => $"[0x{t.Address:X} {t.Index}] {t.Reputation}")));
    return 0;
}

// nationcap <name>: RE (read only), the City at nation+0xE8 and the Continent at nation+0xF0 for the nations whose name matches.
int NationCapCommand(string query)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    string? Str(ulong field) { ulong sp = BitConverter.ToUInt64(mem.Read(field, 8)); if (sp == 0) return null; var l = new byte[4]; if (!mem.TryRead(sp, l)) return null; int n = BitConverter.ToInt32(l); return n is >= 0 and <= 300 ? System.Text.Encoding.UTF8.GetString(mem.Read(sp + 4, n)) : null; }
    foreach (var n in session.Nations.LoadAll().Where(n => query.Length == 0 || (n.Name ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)).Take(30))
    {
        var city = session.Cities.Read(mem.ReadUInt64(n.Address + 0xE8));
        ulong cont = mem.ReadUInt64(n.Address + 0xF0);
        var stadium = session.Stadiums.Reload(mem.ReadUInt64(n.Address + 0x118));
        Console.WriteLine($"{n.Name,-24} id {n.Id,5} capital {city?.Name,-18} continent 0x{cont:X} {(cont == 0 ? null : Str(cont + 0x18))} stadium {stadium?.DisplayName}");
    }
    Console.WriteLine("continents: " + string.Join(", ", session.NationLinks.Continents(session.Nations.LoadAll()).Select(c => c.Name)));
    return 0;
}

// awards <name>: read only, awards from the game-wide list.
int AwardsCommand(string query)
{
    using var session = Attach(writable: false);
    var all = session.Awards.LoadAll();
    foreach (var a in all.Where(a => query.Length == 0 || a.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(40))
        Console.WriteLine($"0x{a.Address:X} index {a.Index} id {a.Id} rep {a.Reputation} home {a.WinnerHomeReputation} world {a.WinnerWorldReputation}  {a.Name} ({a.ShortName})");
    Console.WriteLine($"{all.Count} awards");
    return 0;
}

// comps <name>: competitions from the active build's game-wide list.
// compinfo <id>: read one competition's editable fields (reputation, substitution rules, colours, winter ball dates).
int CompInfoCommand(int id)
{
    using var session = Attach(writable: false);
    var competition = session.Competitions.LoadAll().SingleOrDefault(c => c.Id == id)
        ?? throw new InvalidOperationException($"no competition with id {id}");
    Console.WriteLine($"{competition.DisplayName} id {competition.Id} index {competition.Index} 0x{competition.Address:X}");
    Console.WriteLine($"  reputation {competition.Reputation}, substitution rules 0x{competition.SubstitutionRules:X2}");
    if (session.CompetitionDetails.Read(competition) is { } details)
    {
        string Position(short? v) => v is null ? "unknown" : v < 0 ? "n/a" : v.Value.ToString();
        string YesNo(byte? v) => v is null ? "unknown" : v != 0 ? "yes" : "no";
        Console.WriteLine($"  details: three-letter '{details.ThreeLetterName ?? "unknown"}', type {details.TypeName ?? "unknown"}, " +
            $"level {details.CompetitionLevel?.ToString() ?? "unknown"}, trophy {details.TrophyName ?? "unknown"}, " +
            $"category {details.LowestOfficialCategoryName ?? "unknown"}");
        Console.WriteLine($"  details: continental {Position(details.ContinentalPosition)}, last {Position(details.LastContinentalPosition)}, " +
            $"extra officials {YesNo(details.UsesExtraOfficials)}, seated only {YesNo(details.SeatedStadiumsOnly)}");
    }
    else Console.WriteLine("  details: unavailable");
    Console.WriteLine($"  foreground #{competition.Foreground:X8} background #{competition.Background:X8} trim #{competition.Trim:X8} winter ball #{competition.WinterBallColour:X8}");
    Console.WriteLine($"  winter ball {competition.WinterBallStart?.ToString("yyyy-MM-dd") ?? "none"} .. {competition.WinterBallEnd?.ToString("yyyy-MM-dd") ?? "none"}");
    return 0;
}

// compprize <id> [PerWin|PerDraw|PerDefeat <value> | Position <number> <value>] --write
int CompVarCommand(string[] args)
{
    bool write = args.Contains("--write");
    using var session = Attach(writable: write);
    var competition = session.Competitions.LoadAll().Single(c => c.Id == int.Parse(args[0]));
    var snapshot = session.CompetitionVar.Read(competition) ?? throw new InvalidOperationException("VAR unavailable");
    Console.WriteLine($"{competition.DisplayName}: type {snapshot.Type} TV {snapshot.TvOnly} enabled {snapshot.Enabled} from {snapshot.FromDate?.ToString("yyyy-MM-dd") ?? "none"}");
    if (write)
    {
        if (args.Length < 4) throw new ArgumentException("compvar <id> <Type|TvOnly|Enabled> <value> --write");
        if (args[1].Equals("FromDate", StringComparison.OrdinalIgnoreCase))
            session.CompetitionVar.SetFromDate(snapshot, args[2].Equals("none", StringComparison.OrdinalIgnoreCase) ? null : DateOnly.ParseExact(args[2], "yyyy-MM-dd"));
        else session.CompetitionVar.Set(snapshot, Enum.Parse<CompetitionVarField>(args[1], true), byte.Parse(args[2]));
        var after = session.CompetitionVar.Read(competition)!;
        Console.WriteLine($"read back: type {after.Type} TV {after.TvOnly} enabled {after.Enabled} from {after.FromDate?.ToString("yyyy-MM-dd") ?? "none"}");
    }
    return 0;
}

int CompStandingsCommand(string[] args)
{
    int id = int.Parse(args[0]);
    bool write = args.Contains("--write");
    using var session = Attach(writable: write);
    var competition = session.Competitions.LoadAll().Single(c => c.Id == id);
    var rows = session.CompetitionStandings.Read(competition) ?? throw new InvalidOperationException("Standings unavailable");
    var clubs = session.LoadClubs(session.Players.LoadAll()).ToDictionary(c => c.Address);
    foreach (var row in rows)
        Console.WriteLine($"{row.Position,2} {clubs.GetValueOrDefault(row.Club)?.DisplayName ?? row.ClubId.ToString(),-25} ID {row.ClubId} P {row.Played} W {row.Won} D {row.Drawn} L {row.Lost} GF {row.GoalsFor & 0xFF} GA {row.GoalsAgainst & 0xFF} Pts {row.Points}");
    if (write)
    {
        if (args.Length < 4) throw new ArgumentException("compstandings <competition-id> <club-id> [field] <value> --write");
        var target = rows.Single(row => row.ClubId == int.Parse(args[1]));
        var field = args.Length >= 5 ? Enum.Parse<CompetitionStandingField>(args[2], true) : CompetitionStandingField.Points;
        int value = int.Parse(args[args.Length >= 5 ? 3 : 2]);
        session.CompetitionStandings.Set(competition, target, field, value);
        var updated = session.CompetitionStandings.Read(competition)?.Single(row => row.ClubId == target.ClubId);
        Console.WriteLine($"read back: {(updated is null ? "unavailable" : CompetitionStandingsAccess.Value(updated, field).ToString())}");
    }
    return 0;
}

int CompRulesCommand(string[] args)
{
    int id = int.Parse(args[0]);
    bool write = args.Contains("--write");
    using var session = Attach(writable: write);
    var competitions = session.Competitions.LoadAll();
    var competition = competitions.Single(c => c.Id == id);
    var access = session.CompetitionSquadRules;
    if (args.Length > 2 && args[1].Equals("code", StringComparison.OrdinalIgnoreCase))
    {
        int code = Convert.ToInt32(args[2], 16);
        Action<string>? trace = args.Contains("--trace") ? Console.WriteLine : null;
        var snapshot = access.ReadRule(competition, code, trace, competitions) ?? throw new InvalidOperationException("Rule unavailable or ambiguous");
        Console.WriteLine($"{id} {competition.DisplayName}: rule 0x{code:X} = {snapshot.MaximumSquadSize}");
        if (snapshot.AlsoAppliesTo.Count > 0)
            Console.WriteLine($"also applies to: {string.Join(", ", snapshot.AlsoAppliesTo)}");
        if (write)
        {
            if (args.Length < 5) throw new ArgumentException("comprules <id> code <hex-code> <value> --write");
            access.SetMaximumSquadSize(snapshot, int.Parse(args[3]));
            Console.WriteLine($"read back: {access.ReadRule(competition, code)?.MaximumSquadSize.ToString() ?? "unavailable"}");
        }
        return 0;
    }
    if (args.Length > 1 && (args[1].Equals("homegrown", StringComparison.OrdinalIgnoreCase) || args[1].Equals("homegrownclub", StringComparison.OrdinalIgnoreCase)))
    {
        bool club = args[1].Equals("homegrownclub", StringComparison.OrdinalIgnoreCase);
        CompetitionSquadRules? Read() => club ? access.ReadHomegrownClub(competition) : access.ReadHomegrown(competition);
        var homegrown = Read() ?? throw new InvalidOperationException("Homegrown rule is unavailable");
        Console.WriteLine($"{id} {competition.DisplayName}: Homegrown {(club ? "At Club" : "in Nation")} {homegrown.MaximumSquadSize}");
        if (write)
        {
            if (args.Length < 4) throw new ArgumentException("comprules <id> homegrown <value> --write");
            access.SetMaximumSquadSize(homegrown, int.Parse(args[2]));
            Console.WriteLine($"read back: {Read()?.MaximumSquadSize.ToString() ?? "unavailable"}");
        }
        return 0;
    }
    var rules = access.Read(competition) ?? throw new InvalidOperationException("Squad rules are unavailable or ambiguous");
    Console.WriteLine($"{id} {competition.DisplayName}: Maximum squad size {rules.MaximumSquadSize}");
    if (!write) return 0;
    if (args.Length < 3) throw new ArgumentException("comprules <id> <maximum> --write");
    access.SetMaximumSquadSize(rules, int.Parse(args[1]));
    Console.WriteLine($"read back: {access.Read(competition)?.MaximumSquadSize.ToString() ?? "unavailable"}");
    return 0;
}

int CompPrizeCommand(string[] args)
{    int id = int.Parse(args[0]);
    bool write = args.Contains("--write");
    using var session = Attach(writable: write);
    var competition = session.Competitions.LoadAll().Single(c => c.Id == id);
    var access = session.CompetitionPrizeMoney;
    var prize = access.Read(competition) ?? throw new InvalidOperationException("Prize Money is unavailable for this competition or build");
    Console.WriteLine($"{id} {competition.DisplayName}: win {prize.PerWin}, draw {prize.PerDraw}, defeat {prize.PerDefeat}");
    var classification = access.ReadClassification(prize);
    if (classification is not null)
        Console.WriteLine($"classification: {string.Join(", ", classification.Amounts.Select((v, i) => $"{i + 1}={v}"))}");
    if (!write) return 0;
    if (args.Length < 3) throw new ArgumentException("compprize <id> <field> <value> --write");
    if (args[1].Equals("Enabled", StringComparison.OrdinalIgnoreCase))
    {
        if (classification is null || args.Length < 4 || !bool.TryParse(args[2], out bool enabled))
            throw new ArgumentException("compprize <id> Enabled <true|false> --write");
        session.SetCompetitionClassificationEnabled(classification, enabled);
        var result = access.ReadClassification(prize) ?? throw new StaleObjectException("Classification prizes are no longer readable");
        Console.WriteLine($"read back: enabled {result.Amounts.Count > 0}, positions {result.Amounts.Count}");
        return 0;
    }
    if (args[1].Equals("Position", StringComparison.OrdinalIgnoreCase))
    {
        if (args.Length < 5 || classification is null) throw new ArgumentException("compprize <id> Position <number> <value> --write");
        int position = int.Parse(args[2]);
        access.SetClassification(classification, position, int.Parse(args[3]));
        var positions = access.ReadClassification(prize) ?? throw new StaleObjectException("Classification prizes are no longer readable");
        Console.WriteLine($"read back: position {position} = {positions.Amounts[position - 1]}");
        return 0;
    }
    var field = Enum.Parse<CompetitionPrizeField>(args[1], true);
    access.Set(prize, field, int.Parse(args[2]));
    var after = access.Read(competition) ?? throw new StaleObjectException("Prize group is no longer readable");
    Console.WriteLine($"read back: win {after.PerWin}, draw {after.PerDraw}, defeat {after.PerDefeat}");
    return 0;
}

// compset <id> <subs|fg|bg|trim|wb|wbstart|wbend|rep> <value>  (colour = #AARRGGBB, date = yyyy-MM-dd or none)
int CompSetCommand(string[] args)
{
    int id = int.Parse(args[0]);
    string field = args[1].ToLowerInvariant();
    string value = args[2];
    using var session = Attach(writable: true);
    var competition = session.Competitions.LoadAll().SingleOrDefault(c => c.Id == id)
        ?? throw new InvalidOperationException($"no competition with id {id}");
    var writer = session.CompetitionWriter;
    switch (field)
    {
        case "subs":
            writer.SetSubstitutionRules(competition, value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToByte(value[2..], 16) : byte.Parse(value));
            break;
        case "fg": case "bg": case "trim": case "wb":
            var colour = (CompetitionColour)Array.IndexOf(new[] { "fg", "bg", "trim", "wb" }, field);
            writer.SetColour(competition, colour, Convert.ToUInt32(value.TrimStart('#'), 16));
            break;
        case "wbstart": case "wbend":
            writer.SetWinterBallDate(competition, field == "wbstart", value.Equals("none", StringComparison.OrdinalIgnoreCase)
                ? null : DateOnly.ParseExact(value, "yyyy-MM-dd"));
            break;
        case "rep":
            writer.SetReputation(competition, short.Parse(value));
            break;
        default:
            throw new ArgumentException("field must be subs, fg, bg, trim, wb, wbstart, wbend or rep");
    }
    var after = session.Competitions.Reload(competition.Address) ?? throw new StaleObjectException("Competition is no longer readable");
    Console.WriteLine($"{after.DisplayName} id {after.Id}: reputation {after.Reputation}, subs 0x{after.SubstitutionRules:X2}, " +
        $"fg #{after.Foreground:X8} bg #{after.Background:X8} trim #{after.Trim:X8} wb #{after.WinterBallColour:X8}, " +
        $"{after.WinterBallStart?.ToString("yyyy-MM-dd") ?? "none"} .. {after.WinterBallEnd?.ToString("yyyy-MM-dd") ?? "none"}");
    return 0;
}

// compviaclubs: RE (read only). FM Studio 1.1.1 finds competitions without a list root: [team+teamCompetition] and
// [club+0x310] of every club, then objects next to each hit at a 0x1B0 stride. This prints what that finds on the running game.
int CompViaClubsCommand(string[] rest)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var c = session.Profile.Competition ?? throw new InvalidOperationException("Profile has no competition offsets");
    ulong mb = session.Game.ModuleBase, vt = mb + c.VtableRva;
    var q = new byte[8];
    ulong Ptr(ulong at) => mem.TryRead(at, q) ? BitConverter.ToUInt64(q) : 0;
    bool Canon(ulong p) => p >= 0x10000 && p <= 0x7FFFFFFFFFFF;
    bool IsComp(ulong p) => Canon(p) && Ptr(p) == vt;
    var clubs = session.LoadClubs(session.Players.LoadAll());
    var vtHist = new Dictionary<ulong, int>();
    var seeds = new HashSet<ulong>();
    int viaTeam = 0, viaClub = 0, offCandidates = 0;
    foreach (var club in clubs)
    {
        foreach (var team in club.Teams)
        {
            ulong p = Ptr(team.Address + (ulong)c.TeamCompetition);
            if (!Canon(p)) continue;
            offCandidates++;
            ulong v = Ptr(p);
            vtHist[v] = vtHist.GetValueOrDefault(v) + 1;
            if (v == vt && seeds.Add(p)) viaTeam++;
        }
        ulong q2 = Ptr(club.Address + 0x310);
        if (Canon(q2) && Ptr(q2) == vt && seeds.Add(q2)) viaClub++;
    }
    Console.WriteLine($"clubs {clubs.Count}, team+0x{c.TeamCompetition:X} candidates {offCandidates}, seeds via team {viaTeam}, via club+0x310 {viaClub}");
    foreach (var (v, n) in vtHist.OrderByDescending(kv => kv.Value).Take(6))
        Console.WriteLine($"  vtable 0x{v:X} (module+0x{(v >= mb ? v - mb : 0):X}) x{n}{(v == vt ? "  <- profile competition vtable" : "")}");
    var all = new HashSet<ulong>(seeds);
    foreach (var seed in seeds)
        foreach (int step in new[] { 0x1B0, -0x1B0 })
            for (ulong p = seed; ; )
            {
                p = step > 0 ? p + (ulong)step : p - (ulong)(-step);
                if (!IsComp(p) || !all.Add(p)) break;
            }
    Console.WriteLine($"competition objects after stride walk: {all.Count}");
    var repo = session.Competitions;
    int named = 0;
    foreach (var p in all) if (repo.Reload(p) is { Name: not null }) named++;
    Console.WriteLine($"readable with a name: {named}");
    foreach (var p in all.Take(5)) if (repo.Reload(p) is { } r) Console.WriteLine($"  0x{p:X} id {r.Id} {r.Name}");
    return 0;
}

// preset <uefa|club|nation|cohesion|improve|destroy> <player id>   run the editor's Information-page preset on one player
// (writes to the game) and print the player's relations before and after.
int PresetCommand(string[] rest)
{
    if (rest.Length >= 1 && rest[0] == "scan")
    {
        // preset scan [club name]: players of the club that lack each thing the presets add, with their IDs
        using var scanSession = Attach(writable: false);
        string? only = rest.Length > 1 ? string.Join(' ', rest[1..]) : null;
        var cohesion = new TeamCohesionWriter(scanSession.Memory, scanSession.Profile, scanSession.ModuleBase);
        int shown = 0;
        var kinds = new SortedDictionary<int, int>();
        foreach (var p in scanSession.Players.LoadAll())
            foreach (var r in scanSession.ReadStatuses(p).Registrations) kinds[r.Kind] = kinds.GetValueOrDefault(r.Kind) + 1;
        Console.WriteLine("registration kinds (all players): " + string.Join(" ", kinds.Select(k => $"{k.Key}×{k.Value}")));
        foreach (var p in scanSession.Players.LoadAll().Where(p => p.Club is not null && (only is null || p.Club.DisplayName.Contains(only, StringComparison.OrdinalIgnoreCase))))
        {
            var s = scanSession.ReadStatuses(p);
            var missing = new List<string>();
            if (!s.Registrations.Any(r => r.Kind == 0)) missing.Add("uefa");
            if (!s.TrainedAtClubs.Any()) missing.Add("club-rows");
            if (!s.TrainedAtNations.Any()) missing.Add("nation-rows");
            if (cohesion.Read(p, p.Club!.Address) is null) missing.Add("cohesion");
            if (missing.Count == 0 || missing.All(m => m == "uefa")) continue;
            Console.WriteLine($"{p.Id} {p.DisplayName} ({p.Club.DisplayName}) lacks: {string.Join(", ", missing)}");
            if (++shown >= 25) break;
        }
        return 0;
    }
    if (rest.Length < 2) { Console.Error.WriteLine("usage: preset <uefa|club|nation|cohesion|cohesiondestroy> <player id> | preset scan [club] | preset heal|inspire|destroy|allinone|show <id>... [--check]"); return 2; }
    using var session = Attach(writable: true);
    int id = int.Parse(rest[1]);
    var player = session.Players.LoadAll().SingleOrDefault(p => p.Id == id) ?? throw new InvalidOperationException($"No player with ID {id}");
    void Print(string title)
    {
        var s = session.ReadStatuses(player);
        Console.WriteLine($"-- {title}: {player.DisplayName} ({player.Club?.DisplayName})");
        foreach (var r in s.Registrations) Console.WriteLine($"  registered {r.Club?.DisplayName} kind {r.Kind}");
        foreach (var c in s.TrainedAtClubs) Console.WriteLine($"  trained at club {c.Club?.DisplayName} period {c.Period}");
        foreach (var n in s.TrainedAtNations) Console.WriteLine($"  trained at nation {n.Nation?.DisplayName} period {n.Period}");
        if (player.Club is { } club)
            Console.WriteLine($"  cohesion with {club.DisplayName}: {new TeamCohesionWriter(session.Memory, session.Profile, session.ModuleBase).Read(player, club.Address)?.ToString() ?? "none"}");
        var teamOff = session.Profile.Team!;
        ulong playingTeam = session.Memory.ReadUInt64(player.Address + (ulong)teamOff.PlayerTeam);
        ulong playingClub = playingTeam == 0 ? 0 : session.Memory.ReadUInt64(playingTeam + (ulong)session.Profile.Contract!.TeamClub);
        if (playingClub != 0 && playingClub != player.Club?.Address)
            Console.WriteLine($"  playing team 0x{playingTeam:X} club 0x{playingClub:X} (contract club 0x{player.Club?.Address:X}) cohesion there: {new TeamCohesionWriter(session.Memory, session.Profile, session.ModuleBase).Read(player, playingClub)?.ToString() ?? "none"}");
    }
    Print("before");
    bool changed = rest[0].ToLowerInvariant() switch
    {
        "uefa" => session.RegisterForUefa(player),
        "club" => session.TrainedAtCurrentClub(player),
        "nation" => session.TrainedInCurrentNation(player),
        "cohesion" => session.SetTeamCohesion(player, TeamCohesionWriter.Improve),
        "cohesiondestroy" => session.SetTeamCohesion(player, TeamCohesionWriter.Destroy),
        _ => throw new ArgumentException("unknown preset " + rest[0])
    };
    Console.WriteLine($"preset {rest[0]} returned {changed}");
    Print("after");
    return 0;
}

int CompsCommand(string query)
{
    using var session = Attach(writable: false);
    var all = session.Competitions.LoadAll();
    foreach (var competition in all.Where(c => c.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(25))
        Console.WriteLine($"0x{competition.Address:X} index {competition.Index} id {competition.Id} {competition.Name} ({competition.ShortName})");
    Console.WriteLine($"{all.Count} competitions");
    return 0;
}

// compsnap <path>: RE (read only). Save every competition object (0x1B0 bytes) keyed by competition ID, so a later
// compdiff can locate what FMRTE changed even when the save reload moves every address.
int CompSnapCommand(string path)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var lines = new List<string>();
    foreach (var competition in session.Competitions.LoadAll())
        lines.Add($"{competition.Id}\t{Convert.ToHexString(mem.Read(competition.Address, 0x1B0))}");
    File.WriteAllLines(path, lines);
    Console.WriteLine($"{lines.Count} competition objects -> {path}");
    return 0;
}

// compdiff <path>: RE (read only). Compare the saved snapshot with the live objects by competition ID.
int CompDiffCommand(string path)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var before = new Dictionary<int, byte[]>();
    foreach (var line in File.ReadAllLines(path))
    {
        var parts = line.Split('\t');
        if (parts.Length == 2 && int.TryParse(parts[0], out int id)) before[id] = Convert.FromHexString(parts[1]);
    }
    int changed = 0;
    foreach (var competition in session.Competitions.LoadAll())
    {
        if (!before.TryGetValue(competition.Id, out var old)) continue;
        var now = mem.Read(competition.Address, old.Length);
        var diffs = Enumerable.Range(0, old.Length).Where(i => old[i] != now[i]).ToArray();
        if (diffs.Length == 0) continue;
        changed++;
        if (changed > 40) continue;
        Console.WriteLine($"id {competition.Id} {competition.DisplayName}: {diffs.Length} bytes differ");
        int at = 0;
        while (at < diffs.Length)
        {
            int start = diffs[at], end = start;
            while (at + 1 < diffs.Length && diffs[at + 1] == end + 1) { at++; end++; }
            at++;
            int count = end - start + 1;
            Console.WriteLine($"  +0x{start:X3} ({count}) {Convert.ToHexString(old, start, count)} -> {Convert.ToHexString(now, start, count)}");
        }
    }
    Console.WriteLine($"{changed} competitions changed");
    return 0;
}

// compscan <start hex> <len>: RE (read only). For every byte offset in the range, list how many distinct values the
// whole competition list has and the most common ones, so enum-like fields stand out from per-object pointers.
int CompScanCommand(int start, int len)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    ulong mb = session.Game.ModuleBase;
    var offsets = session.Profile.Competition ?? throw new InvalidOperationException("Profile has no competition offsets");
    var bodies = new List<(ulong Address, byte[] Bytes, int Id)>();
    foreach (var competition in session.Competitions.LoadAll())
    {
        var b = mem.Read(competition.Address, 0x1B0);
        if (BitConverter.ToUInt64(b) != mb + offsets.VtableRva) continue;
        bodies.Add((competition.Address, b, competition.Id));
    }
    Console.WriteLine($"{bodies.Count} competition objects");
    for (int o = start; o < start + len && o < 0x1B0; o++)
    {
        var counts = new Dictionary<byte, int>();
        foreach (var (_, b, _) in bodies)
        {
            counts.TryGetValue(b[o], out int n);
            counts[b[o]] = n + 1;
        }
        if (counts.Count > 12) continue; // pointers / high-entropy data
        var parts = counts.OrderByDescending(k => k.Value).Take(8)
            .Select(k => $"{(char.IsControl((char)k.Key) ? "?" : ((char)k.Key).ToString())}/{k.Key:X2}x{k.Value}");
        Console.WriteLine($"+0x{o:X3} {counts.Count,2} values: {string.Join(" ", parts)}");
    }
    return 0;
}

// compsurvey <offset hex> <len>: RE (read only), histogram of the bytes at competition+offset over the whole list, with example names.
int CompSurveyCommand(int offset, int len)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    ulong mb = session.Game.ModuleBase, root = mem.ReadUInt64(mb + 0x56F5070);
    ulong begin = mem.ReadUInt64(root + 0x40), end = mem.ReadUInt64(root + 0x48);
    var raw = mem.Read(begin, (int)(end - begin));
    string? Str(ulong field) { ulong sp = BitConverter.ToUInt64(mem.Read(field, 8)); if (sp == 0) return null; var l = new byte[4]; if (!mem.TryRead(sp, l)) return null; int n = BitConverter.ToInt32(l); return n is >= 0 and <= 300 ? System.Text.Encoding.UTF8.GetString(mem.Read(sp + 4, n)) : null; }
    var hist = new Dictionary<string, (int Count, List<string> Names)>();
    for (int i = 0; i < raw.Length; i += 8)
    {
        ulong p = BitConverter.ToUInt64(raw, i);
        var b = new byte[offset + len];
        if (!mem.TryRead(p, b) || BitConverter.ToUInt64(b) != mb + 0x4CDFAD8) continue;
        string hex = Convert.ToHexString(b, offset, len);
        var e = hist.TryGetValue(hex, out var x) ? x : (0, []);
        if (e.Names.Count < 3) e.Names.Add($"{Str(p + 0x40)} (id {BitConverter.ToInt32(b, 0xC)})");
        hist[hex] = (e.Item1 + 1, e.Names);
    }
    Console.WriteLine($"{hist.Count} distinct values");
    foreach (var (hex, (count, names)) in hist.OrderByDescending(k => k.Value.Count).Take(80))
        Console.WriteLine($"  {hex}  x{count}  {string.Join("; ", names)}");
    return 0;
}

int ClubStaffCommand(int clubId)
{
    using var session = Attach(writable: false);
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId)
        ?? session.ScanAllClubs().SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var today = session.ReadGameDate();
    var staff = session.Staff.LoadAll().Where(s => s.Club?.Address == club.Address).ToArray();
    Console.WriteLine($"{club.Name}: {staff.Length} staff");
    foreach (var s in staff)
    {
        int? age = today is { } t && s.DateOfBirth is { } b ? t.Year - b.Year - (t < b.AddYears(t.Year - b.Year) ? 1 : 0) : null;
        Console.WriteLine($"  job {s.JobCode,3} {s.DisplayName,-26} age {age,3} {s.Nationality?.DisplayName,-22} CA {s.CurrentAbility,3} PA {s.PotentialAbility,3} 0x{s.Address:X}");
    }
    return 0;
}

// clubrel <id>: list. setclubrel <id> <row> <reason|-> <value|-> [permanent|temporary]: guarded write of one row.
int ClubRelCommand(int clubId, string[]? set)
{
    using var session = Attach(writable: set is not null);
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId)
        ?? session.ScanAllClubs().SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var rows = session.ReadClubRelationships(club.Address);
    if (set is not null)
    {
        var row = rows[int.Parse(set[0])];
        session.SetClubRelationship(club.Address, row, set[1] == "-" ? null : int.Parse(set[1]),
            set.Length > 3 ? set[3] == "permanent" : null, set[2] == "-" ? null : int.Parse(set[2]));
        rows = session.ReadClubRelationships(club.Address);
    }
    Console.WriteLine($"{club.Name}: {rows.Count} relationship rows");
    for (int i = 0; i < rows.Count; i++)
    {
        var r = rows[i];
        string type = r.Kind == Fm27Editor.Core.Clubs.ClubRelationshipKind.Other ? $"Unknown ({r.TypeCode})" : r.Kind.ToString();
        Console.WriteLine($"  {i,2} 0x{r.Address:X} {type,-15} {r.Name,-28} value {r.Value,3} reason {r.Reason} permanent {r.Permanent}  {r.Snapshot}");
    }
    return 0;
}


// addclubrel <clubId> <rivalClubId> [value]: appends a Rival row into the vector's spare capacity (write)
int ClubRelAddCommand(int clubId, int rivalId, int value)
{
    using var session = Attach(writable: true);
    var clubs = session.LoadClubs(session.Players.LoadAll());
    var club = clubs.SingleOrDefault(c => c.Id == clubId) ?? session.ScanAllClubs().SingleOrDefault(c => c.Id == clubId);
    var rival = clubs.SingleOrDefault(c => c.Id == rivalId) ?? session.ScanAllClubs().SingleOrDefault(c => c.Id == rivalId);
    if (club is null || rival is null) { Console.Error.WriteLine("both clubs must exist"); return 2; }
    var added = new Fm27Editor.Core.Clubs.ClubRelationshipWriter(session.Memory, session.Profile, session.Game.ModuleBase).AddRival(club.Address, rival.Address, value, 2, session.AllocateGameBlock);
    Console.WriteLine($"{club.Name} += rival {rival.Name}: {(added is null ? "not added" : $"0x{added.Address:X} value {added.Value}")}");
    foreach (var r in session.ReadClubRelationships(club.Address)) Console.WriteLine($"  {r.Kind,-15} {r.Name,-28} value {r.Value,3} reason {r.Reason}");
    return added is null ? 1 : 0;
}

// delclubrel <clubId> <row>: removes one row and shifts the rest left (write)
int ClubRelDeleteCommand(int clubId, int row)
{
    using var session = Attach(writable: true);
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId)
        ?? session.ScanAllClubs().SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var rows = session.ReadClubRelationships(club.Address);
    if (row < 0 || row >= rows.Count) { Console.Error.WriteLine($"club has {rows.Count} rows"); return 2; }
    new Fm27Editor.Core.Clubs.ClubRelationshipWriter(session.Memory, session.Profile, session.Game.ModuleBase).Remove(club.Address, rows[row]);
    Console.WriteLine($"{club.Name}: removed row {row}");
    foreach (var r in session.ReadClubRelationships(club.Address)) Console.WriteLine($"  {r.Kind,-15} {r.Name,-28} value {r.Value,3} reason {r.Reason}");
    return 0;
}


// addclubvis <clubId> <row>: adds a vision row by copying that row into a new culture object (write)
int ClubVisAddCommand(int clubId, int row)
{
    using var session = Attach(writable: true);
    var clubs = session.LoadClubs(session.Players.LoadAll());
    var club = clubs.SingleOrDefault(c => c.Id == clubId) ?? session.ScanAllClubs().SingleOrDefault(c => c.Id == clubId);
    var team = club?.FirstTeam ?? throw new InvalidOperationException($"No first team for club {clubId}");
    var rows = session.ReadClubVision(team.Address, clubs);
    if (row < 0 || row >= rows.Count) { Console.Error.WriteLine($"club has {rows.Count} vision rows"); return 2; }
    var added = new Fm27Editor.Core.Clubs.ClubVisionWriter(session.Memory, session.Profile, session.Game.ModuleBase)
        .Duplicate(team.Address, rows[row], session.AllocateGameBlock);
    Console.WriteLine($"{club.Name}: duplicated row {row} -> {(added is null ? "not added" : $"0x{added.Address:X} type {added.TypeCode} importance {added.Importance}")}");
    foreach (var r in session.ReadClubVision(team.Address, clubs)) Console.WriteLine($"  {r.Address:X} type {r.TypeCode,3} importance {r.Importance,3} {r.StartDate}..{r.EndDate} club {r.ClubItem?.Name}");
    return added is null ? 1 : 0;
}

// delclubvis <clubId> <row>: removes one vision row (write)
int ClubVisDeleteCommand(int clubId, int row)
{
    using var session = Attach(writable: true);
    var clubs = session.LoadClubs(session.Players.LoadAll());
    var club = clubs.SingleOrDefault(c => c.Id == clubId) ?? session.ScanAllClubs().SingleOrDefault(c => c.Id == clubId);
    var team = club?.FirstTeam ?? throw new InvalidOperationException($"No first team for club {clubId}");
    var rows = session.ReadClubVision(team.Address, clubs);
    if (row < 0 || row >= rows.Count) { Console.Error.WriteLine($"club has {rows.Count} vision rows"); return 2; }
    new Fm27Editor.Core.Clubs.ClubVisionWriter(session.Memory, session.Profile, session.Game.ModuleBase).Remove(team.Address, rows[row]);
    Console.WriteLine($"{club.Name}: removed row {row}");
    foreach (var r in session.ReadClubVision(team.Address, clubs)) Console.WriteLine($"  {r.Address:X} type {r.TypeCode,3} importance {r.Importance,3} club {r.ClubItem?.Name}");
    return 0;
}


// addclubaff <clubId> <row>: adds a link by copying that affiliation record into a new object (write)
int ClubAffAddCommand(int clubId, int row)
{
    using var session = Attach(writable: true);
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId)
        ?? session.ScanAllClubs().SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var rows = session.ReadAffiliations(club.Address);
    if (row < 0 || row >= rows.Count) { Console.Error.WriteLine($"club has {rows.Count} affiliations"); return 2; }
    var added = new Fm27Editor.Core.Clubs.ClubAffiliationWriter(session.Memory, session.Profile, session.Game.ModuleBase)
        .Duplicate(club.Address, rows[row], session.AllocateGameBlock);
    Console.WriteLine($"{club.Name}: duplicated affiliation {row} -> {(added is null ? "not added" : $"0x{added.Address:X} type {added.TypeCode}")}");
    foreach (var r in session.ReadAffiliations(club.Address)) Console.WriteLine($"  {r.Address:X} {r.ParentClub?.Name} <- {r.FeederClub?.Name} type {r.TypeCode} fee {r.AnnualFee}");
    return added is null ? 1 : 0;
}

// delclubaff <clubId> <row>: removes one affiliation pointer (write)
int ClubAffDeleteCommand(int clubId, int row)
{
    using var session = Attach(writable: true);
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId)
        ?? session.ScanAllClubs().SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var rows = session.ReadAffiliations(club.Address);
    if (row < 0 || row >= rows.Count) { Console.Error.WriteLine($"club has {rows.Count} affiliations"); return 2; }
    new Fm27Editor.Core.Clubs.ClubAffiliationWriter(session.Memory, session.Profile, session.Game.ModuleBase).Remove(club.Address, rows[row]);
    Console.WriteLine($"{club.Name}: removed affiliation {row}");
    foreach (var r in session.ReadAffiliations(club.Address)) Console.WriteLine($"  {r.Address:X} {r.ParentClub?.Name} <- {r.FeederClub?.Name} type {r.TypeCode}");
    return 0;
}


// nationcont <nation>: continent rows of the nation's transfer preferences (read only)
// setnationcont <nation> <row> <rating> <minAge> <maxAge> <knowledge>: guarded write of one row
int NationContCommand(string query, string[]? set)
{
    using var session = Attach(writable: set is not null);
    var name = query.Trim();
    var nations = session.Nations.LoadAll();
    var nation = nations.FirstOrDefault(n => n.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"no nation named '{name}'");
    var continents = session.NationLinks.Continents(nations);
    var rows = session.NationPreferences.ReadContinents(nation, continents);
    Console.WriteLine($"{nation.DisplayName} id {nation.Id} 0x{nation.Address:X}: {rows.Count} continent rows");
    for (int i = 0; i < rows.Count; i++)
        Console.WriteLine($"  {i,2} 0x{rows[i].Address:X} {rows[i].Target,-16} rating {rows[i].Rating,3} scouting {rows[i].Knowledge,3} min {rows[i].MinAge,3} max {rows[i].MaxAge,3}");
    if (set is not { } values) return 0;
    int row = int.Parse(values[0]);
    if (row < 0 || row >= rows.Count) { Console.Error.WriteLine($"nation has {rows.Count} continent rows"); return 2; }
    session.NationPreferences.SetContinent(rows[row], int.Parse(values[1]), int.Parse(values[2]), int.Parse(values[3]), int.Parse(values[4]));
    var after = session.NationPreferences.ReadContinents(nation, continents).FirstOrDefault(r => r.Address == rows[row].Address);
    Console.WriteLine(after is null ? "read back failed" : $"read back: rating {after.Rating} scouting {after.Knowledge} min {after.MinAge} max {after.MaxAge}");
    return 0;
}


// nationcoeff <nation>: FMRTE's EuroCoefficients rows (read only)
int NationCoeffCommand(string query)
{
    using var session = Attach(writable: false);
    var name = query.Trim();
    var nation = session.Nations.LoadAll().FirstOrDefault(n => n.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"no nation named '{name}'");
    var values = session.NationEuroCoefficients.Read(nation.Address);
    int season = session.ReadGameDate()?.Year ?? 0;
    var labels = Fm27Editor.Core.Nations.NationEuroCoefficients.Labels(season, values.Count);
    Console.WriteLine($"{nation.DisplayName} id {nation.Id}: {values.Count} coefficients");
    for (int i = 0; i < values.Count; i++) Console.WriteLine($"  {labels[i],-18} {values[i]:0.###}");
    return 0;
}


// nationagr <nation> [eu|nf]                       list FMRTE's Agreements rows (read only)
// addnationagr <nation> <eu|nf> <row>              duplicate one row like FMRTE's + (write)
// delnationagr <nation> <eu|nf> <row>              remove one row (write)
int NationAgrCommand(string[] args, string[]? unused)
{
    bool setRegion = args.Length == 5 && args[2].Equals("region", StringComparison.OrdinalIgnoreCase);
    bool editList = args.Length >= 3 && args[0] is "add" or "del";
    bool write = editList || setRegion;
    var words = editList ? args[1..] : args;
    bool add = write && args[0] == "add";
    using var session = Attach(writable: write);
    var nations = session.Nations.LoadAll();
    var nation = nations.FirstOrDefault(n => n.DisplayName.Equals(words[0], StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"no nation named '{words[0]}'");
    int vector = words.Length > 1 && words[1].Equals("nf", StringComparison.OrdinalIgnoreCase)
        ? Fm27Editor.Core.Nations.NationAgreements.NonForeignVector
        : Fm27Editor.Core.Nations.NationAgreements.EuVector;
    var continents = session.NationLinks.Continents(nations);
    var rows = session.NationAgreements.Read(nation.Address, vector, continents, nations);
    if (write)
    {
        int index = setRegion ? int.Parse(words[3]) : words.Length > 2 ? int.Parse(words[2]) : 0;
        if (index < 0 || index >= rows.Count) { Console.Error.WriteLine($"nation has {rows.Count} rows in that list"); return 2; }
        if (setRegion)
        {
            int id = int.Parse(words[4]);
            var region = session.NationAgreements.Regions().FirstOrDefault(r => r.Id == id)
                ?? throw new InvalidOperationException($"no region with ID {id}");
            session.NationAgreements.SetTarget(nation.Address, rows[index], Fm27Editor.Core.Nations.AgreementKind.Region, region.Address);
        }
        else if (add)
            session.NationAgreements.Duplicate(nation.Address, rows[index], session.AllocateGameBlock, continents);
        else
            session.NationAgreements.Remove(nation.Address, rows[index]);
        rows = session.NationAgreements.Read(nation.Address, vector, continents, nations);
    }
    Console.WriteLine($"{nation.DisplayName} id {nation.Id} {(vector == Fm27Editor.Core.Nations.NationAgreements.EuVector ? "EU" : "Non-Foreign")}: {rows.Count} rows");
    for (int i = 0; i < rows.Count; i++)
        Console.WriteLine($"  {i,2} 0x{rows[i].Address:X} {rows[i].Kind,-9} {rows[i].TargetName,-38} {rows[i].From:yyyy-MM-dd}..{rows[i].Until:yyyy-MM-dd}");
    return 0;
}

int NationRegionsCommand()
{
    using var session = Attach(writable: false);
    var regions = session.NationAgreements.Regions();
    foreach (var region in regions) Console.WriteLine($"{region.Id,3} {region.Name}");
    Console.WriteLine($"{regions.Count} regions");
    return 0;
}

// nationagrempty <nation> <template nation> <target nation>
// nationagrempty <nation> eu <template nation> <agreement name>
int NationAgrEmptyCommand(string[] args)
{
    bool eu = args.Length == 4 && args[1].Equals("eu", StringComparison.OrdinalIgnoreCase);
    if (!eu && args.Length != 3) throw new ArgumentException("usage: nationagrempty <nation> <template nation> <target nation> | <nation> eu <template nation> <agreement name>");
    using var session = Attach(writable: true);
    var nations = session.Nations.LoadAll();
    NationRecord Find(string name) => nations.FirstOrDefault(n => n.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"no nation named '{name}'");
    var nation = Find(args[0]);
    var templateNation = Find(eu ? args[2] : args[1]);
    var target = eu ? null : Find(args[2]);
    int vector = eu ? NationAgreements.EuVector : NationAgreements.NonForeignVector;
    var continents = session.NationLinks.Continents(nations);
    var template = session.NationAgreements.Read(templateNation.Address, vector, continents, nations)
        .FirstOrDefault(row => eu ? row.Kind == AgreementKind.Agreement && string.Equals(row.TargetName, args[3], StringComparison.OrdinalIgnoreCase) : row.Kind == AgreementKind.Nation)
        ?? throw new InvalidOperationException("template nation has no matching agreement row");
    var expectedHeader = session.Memory.Read(nation.Address + (ulong)vector, 24);
    var added = session.NationAgreements.AddToEmpty(nation.Address, expectedHeader, templateNation.Address, template,
        session.AllocateGameBlock, continents)
        ?? throw new InvalidOperationException("could not read the new agreement");
    if (target is not null && added.Target != target.Address)
    {
        session.NationAgreements.SetTarget(nation.Address, added, AgreementKind.Nation, target.Address);
    }
    foreach (var row in session.NationAgreements.Read(nation.Address, vector, continents, nations))
        Console.WriteLine($"{row.Kind} {row.TargetName} 0x{row.Address:X}");
    return 0;
}


// nationkits <nation> [on|off]: FMRTE's Allow Licensed Kits flag of the senior national team (write when on/off given)
int NationKitsCommand(string line)
{
    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    bool? set = parts.Length > 1 ? parts[1].Equals("on", StringComparison.OrdinalIgnoreCase) : null;
    if (parts.Length > 1 && set is null) throw new ArgumentException("usage: nationkits <nation> [on|off]");
    using var session = Attach(writable: set is not null);
    var nation = session.Nations.LoadAll().FirstOrDefault(n => n.DisplayName.Equals(parts[0], StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"no nation named '{parts[0]}'");
    var teams = session.NationTeams.Read(nation.Address);
    if (teams.Count == 0) { Console.Error.WriteLine($"{nation.DisplayName} has no national teams"); return 2; }
    var senior = teams[0];
    var access = session.NationTeams;
    Console.WriteLine($"{nation.DisplayName} senior team 0x{senior.Address:X}: Allow Licensed Kits = {access.AllowLicensedKits(senior.Address)}");
    if (set is not { } value) return 0;
    access.SetAllowLicensedKits(senior, value);
    Console.WriteLine($"  now {access.AllowLicensedKits(senior.Address)}");
    return 0;
}


// nationcolours <nation>                       read FMRTE's Background / Foreground Colour of the national team
// nationcolours <nation> bg|fg <#RRGGBB>       guarded write of one colour
int NationColoursCommand(string[] args)
{
    if (args.Length < 1) throw new ArgumentException("usage: nationcolours <nation> [bg|fg <#RRGGBB>]");
    bool write = args.Length >= 3;
    using var session = Attach(writable: write);
    var name = args[0];
    var nation = session.Nations.LoadAll().FirstOrDefault(n => n.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"no nation named '{name}'");
    var access = session.NationColours;
    var colours = access.Read(nation.Address) ?? throw new StaleObjectException("Nation colours are not readable");
    Console.WriteLine($"{nation.DisplayName}: background {Fm27Editor.Core.Nations.NationColours.Format(colours.Background)} foreground {Fm27Editor.Core.Nations.NationColours.Format(colours.Foreground)}");
    if (!write) return 0;
    bool background = args[1].Equals("bg", StringComparison.OrdinalIgnoreCase);
    if (!background && !args[1].Equals("fg", StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("pick bg or fg");
    if (!Fm27Editor.Core.Nations.NationColours.TryParse(args[2], out uint bgra)) throw new ArgumentException("colour must be #RRGGBB");
    access.Set(nation.Address, colours, background, bgra);
    var after = access.Read(nation.Address) ?? throw new StaleObjectException("Nation colours are no longer readable");
    Console.WriteLine($"  now background {Fm27Editor.Core.Nations.NationColours.Format(after.Background)} foreground {Fm27Editor.Core.Nations.NationColours.Format(after.Foreground)}");
    return 0;
}


// natpref <nation> [cont]                                  list rows (nations, or continents with "cont")
// natpref <nation> [cont] set <row> <target>               change the target of one row
// natpref <nation> [cont] add <target> [rating min max scouting]
// natpref <nation> [cont] del <row>
int NatPrefCommand(string[] args)
{
    if (args.Length < 1) throw new ArgumentException("usage: natpref <nation> [cont] [set <row> <target> | add <target> ... | del <row>]");
    var nationName = args[0];
    bool continents = args.Length > 1 && args[1].Equals("cont", StringComparison.OrdinalIgnoreCase);
    var rest = continents ? args[2..] : args[1..];
    bool write = rest.Length > 0;
    using var session = Attach(writable: write);
    var nations = session.Nations.LoadAll();
    var nation = nations.FirstOrDefault(n => n.DisplayName.Equals(nationName, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"no nation named '{nationName}'");
    if (session.Profile.Nation?.Preferences is not { } l) throw new InvalidOperationException("Profile has no preference offsets");
    int vector = continents ? Fm27Editor.Core.Nations.NationPreferences.ContinentsVector : l.Vector;
    var allContinents = session.NationLinks.Continents(nations);
    IReadOnlyList<Fm27Editor.Core.Nations.NationPreference> rows = continents
        ? session.NationPreferences.ReadContinents(nation, allContinents)
        : session.NationPreferences.Read(nation, nations);
    ulong ResolveTarget(string name)
    {
        if (continents)
        {
            var c = allContinents.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"no continent named '{name}'");
            return c.Address;
        }
        var other = nations.FirstOrDefault(n => n.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"no nation named '{name}'");
        ulong target = session.Memory.ReadUInt64(other.Address + (ulong)l.Team);
        if (target == 0 || session.Memory.ReadUInt64(target) != session.Game.ModuleBase + l.TeamVtable)
            throw new InvalidOperationException($"{other.DisplayName} has no preference team");
        return target;
    }
    void Reload() => rows = continents ? session.NationPreferences.ReadContinents(nation, allContinents)
                                      : session.NationPreferences.Read(nation, nations);
    if (write)
    {
        var verb = rest[0].ToLowerInvariant();
        if (verb == "set")
        {
            int index = int.Parse(rest[1]);
            if (index < 0 || index >= rows.Count) { Console.Error.WriteLine($"nation has {rows.Count} rows"); return 2; }
            session.NationPreferences.SetTarget(rows[index], vector, ResolveTarget(rest[2]));
            Reload();
        }
        else if (verb == "del")
        {
            int index = int.Parse(rest[1]);
            if (index < 0 || index >= rows.Count) { Console.Error.WriteLine($"nation has {rows.Count} rows"); return 2; }
            session.NationPreferences.Remove(rows[index], vector);
            Reload();
        }
        else if (verb == "add")
        {
            ulong target = ResolveTarget(rest[1]);
            int rating = rest.Length > 2 ? int.Parse(rest[2]) : -1;
            byte minAge = rest.Length > 4 ? byte.Parse(rest[4]) : (byte)14;
            byte maxAge = rest.Length > 5 ? byte.Parse(rest[5]) : (byte)45;
            byte scouting = rest.Length > 6 ? byte.Parse(rest[6]) : (byte)0;
            session.NationPreferences.Add(rows.Count > 0 ? rows[0].Team : throw new InvalidOperationException("Nation has no preference team"),
                vector, target, (sbyte)rating, minAge, maxAge, scouting,
                session.AllocateGameBlock);
            Reload();
        }
        else throw new ArgumentException("verb must be set, add or del");
    }
    Console.WriteLine($"{nation.DisplayName} {(continents ? "continents" : "nations")}: {rows.Count} rows");
    for (int i = 0; i < rows.Count; i++)
        Console.WriteLine($"  {i,2} 0x{rows[i].Address:X} {(rows[i].Target ?? $"0x{rows[i].TargetTeam:X}"),-28} rating {rows[i].Rating,3} scouting {rows[i].Knowledge,3} min {rows[i].MinAge,3} max {rows[i].MaxAge,3}");
    return 0;
}


// nationkitsvendor <nation>                                 list the national team's kit items (Home/Away/Third, types)
// nationkitsvendor <nation> <kit> <type> <index> <#RRGGBB>  write one colour slot
int NationKitItemCommand(string[] args)
{
    if (args.Length < 1) throw new ArgumentException("usage: nationkititem <nation> [kit type index colour]");
    bool write = args.Length >= 5;
    using var session = Attach(writable: write);
    var nation = session.Nations.LoadAll().FirstOrDefault(n => n.DisplayName.Equals(args[0], StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"no nation named '{args[0]}'");
    var kits = session.NationKits;
    var items = kits.Read(nation.Address);
    if (write)
    {
        byte kit = byte.Parse(args[1]);
        byte type = byte.Parse(args[2]);
        int index = int.Parse(args[3]);
        var item = items.FirstOrDefault(x => x.Kit == kit && x.Type == type)
            ?? throw new InvalidOperationException($"no kit item kit={kit} type={type}");
        if (!Fm27Editor.Core.Nations.NationColours.TryParse(args[4], out uint bgra)) throw new ArgumentException("colour must be #RRGGBB");
        kits.SetColour(item, index, bgra);
        items = kits.Read(nation.Address);
    }
    Console.WriteLine($"{nation.DisplayName}: {items.Count} kit items");
    foreach (var item in items)
        Console.WriteLine($"  kit {item.Kit} type 0x{item.Type:X2} style {item.Style,3} flags 0x{item.Flags:X}  " +
            string.Join(" ", Enumerable.Range(0, 4).Select(i => Fm27Editor.Core.Nations.NationColours.Format(item.Colour(i)))));
    return 0;
}


// nationstaff <nation>: national staff, read only.
int NationStaffCommand(string name)
{
    using var session = Attach(writable: false);
    var nation = session.Nations.LoadAll().FirstOrDefault(n => n.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"no nation named '{name}'");
    var staff = session.NationStaff.ReadDetails(nation.Address);
    var gameDate = session.ReadGameDate();
    Console.WriteLine($"{nation.DisplayName}: {staff.Count} staff");
    foreach (var row in staff)
    {
        var s = row.Staff;
        string team = row.Team is null ? "?" : row.TeamType is 0 or 10 or 18 or 11 or 12 ? nation.DisplayName + row.TeamSuffix : $"{nation.DisplayName} (team {row.Team.Index})";
        string age = gameDate is { } today && s.DateOfBirth is { } birth && birth <= today
            ? (today.Year - birth.Year - (today.Month < birth.Month || (today.Month == birth.Month && today.Day < birth.Day) ? 1 : 0)).ToString() : "?";
        string expiry = row.HasFullContract ? row.Expires?.ToString("yyyy-MM-dd") ?? "No limit" : "-";
        Console.WriteLine($"  {s.Id,9} {s.DisplayName,-24} {row.JobName,-22} {team,-22} {s.Nationality?.DisplayName,-20} age {age,3} CA {s.CurrentAbility,3} PA {s.PotentialAbility,3} expires {expiry}");
    }
    return 0;
}

// natcontract <staff id> <show|renew|expire> [--write]   the national-team contract of a staff member (dry run unless --write)
int NationContractCommand(string[] rest)
{
    if (rest.Length < 2) { Console.Error.WriteLine("usage: natcontract <staff id> <show|renew|expire> [--write]"); return 2; }
    bool write = rest.Contains("--write", StringComparer.OrdinalIgnoreCase);
    using var session = Attach(writable: write);
    var staff = session.Staff.LoadAll().FirstOrDefault(s => s.Id.ToString() == rest[0]) ?? throw new InvalidOperationException($"no staff with id {rest[0]}");
    var access = session.NationStaffContracts;
    var contract = access.Read(staff) ?? throw new InvalidOperationException($"{staff.DisplayName} has no national contract");
    var today = session.ReadGameDate() ?? throw new InvalidOperationException("game date unreadable");
    Console.WriteLine($"{staff.DisplayName}: national contract 0x{contract.Address:X} wage {contract.Wage} type {contract.Type} started {contract.Started:yyyy-MM-dd} expires {contract.Expires?.ToString("yyyy-MM-dd") ?? "no limit"} (today {today:yyyy-MM-dd})");
    if (rest[1].Equals("dump", StringComparison.OrdinalIgnoreCase))
    {
        var bytes = session.Memory.Read(contract.Address, 0x120);
        for (int i = 0; i < bytes.Length; i += 16) Console.WriteLine($"  +{i:X3} {Convert.ToHexString(bytes.AsSpan(i, 16))}");
        if (session.Profile.Contract is { } c)
            Console.WriteLine($"  as club-contract offsets: wage {BitConverter.ToInt32(bytes, c.Wage)} type {c.Type.GetValueOrDefault(-1)} started@{c.Started} expiry@{c.ExpiryDate}");
        return 0;
    }
    if (rest[1].Equals("both", StringComparison.OrdinalIgnoreCase)) return 0;
    if (rest[1].Equals("show", StringComparison.OrdinalIgnoreCase)) return 0;
    if (!write) { Console.WriteLine("dry run; pass --write to change it"); return 0; }
    if (rest[1].Equals("renew", StringComparison.OrdinalIgnoreCase)) access.Renew(staff, contract, 4, today);
    else if (rest[1].Equals("expire", StringComparison.OrdinalIgnoreCase)) access.Expire(staff, contract, today);
    else { Console.Error.WriteLine("action must be show, renew or expire"); return 2; }
    var after = access.Read(staff);
    Console.WriteLine($"now expires {after?.Expires?.ToString("yyyy-MM-dd") ?? "no limit"}");
    return 0;
}

// citydump <city id>   the city object bytes and what its pointers lead to (read only)
int CityDumpCommand(string id)
{
    using var session = Attach(writable: false);
    var city = session.Cities.LoadAll().FirstOrDefault(c => c.Id.ToString() == id) ?? throw new InvalidOperationException($"no city {id}");
    var bytes = session.Memory.Read(city.Address, 0x240);
    Console.WriteLine($"{city.Name} id {city.Id} at 0x{city.Address:X}");
    for (int i = 0; i < bytes.Length; i += 16) Console.WriteLine($"  +{i:X3} {Convert.ToHexString(bytes.AsSpan(i, 16))}");
    Console.WriteLine($"module base 0x{session.Game.ModuleBase:X}");
    foreach (int at in new[] { 0x20, 0x28, 0x30, 0x38 })
    {
        ulong target = BitConverter.ToUInt64(bytes, at);
        ulong vt = target == 0 ? 0 : session.Memory.ReadUInt64(target);
        Console.WriteLine($"  +{at:X2} -> 0x{target:X} vtable rva 0x{vt - session.Game.ModuleBase:X}");
    }
    return 0;
}

// promises <player id> [add <type id> [param0 param1] | remove <index> | set <index> <type id> [param0 param1] | dates <index> <made yyyy-mm-dd> <deadline yyyy-mm-dd>] [--write]
int PromisesCommand(string[] rest)
{
    bool write = rest.Contains("--write", StringComparer.OrdinalIgnoreCase);
    var a = rest.Where(x => !x.StartsWith("--")).ToArray();
    using var session = Attach(writable: write);
    var player = session.Players.LoadAll().FirstOrDefault(p => p.Id.ToString() == a[0]) ?? throw new InvalidOperationException($"no player {a[0]}");
    var owner = Fm27Editor.Core.Players.PersonOwner.Of(player, session.Profile);
    var store = session.Promises;
    var staff = session.Staff.LoadAll().ToDictionary(s => s.Address);
    string Name(ulong person) => staff.TryGetValue(person, out var s) ? s.DisplayName : $"0x{person:X}";
    void Show()
    {
        var list = store.Read(owner);
        Console.WriteLine($"{player.DisplayName}: {list.Count} promise(s)");
        for (int i = 0; i < list.Count; i++)
        {
            var p = list[i];
            Console.WriteLine($"  [{i}] 0x{p.Address:X} type {p.TypeId} \"{p.Type?.Text ?? "?"}\" manager {Name(p.Manager)} made {p.Made:yyyy-MM-dd} deadline {p.Deadline:yyyy-MM-dd} p0={p.Parameter0} p1={p.Parameter1} club 0x{p.Club:X}/0x{p.Team:X}");
        }
    }
    Show();
    if (a.Length > 2 && a[1] == "comp")
    {
        int.TryParse(a[2], out int want);
        var allComps = session.Competitions.LoadAll();
        Console.WriteLine($"{allComps.Count} competitions");
        foreach (var c in allComps.Where(c => c.Id == want || c.Index == want || (c.Name?.Contains(a[2], StringComparison.OrdinalIgnoreCase) ?? false)).Take(30))
            Console.WriteLine($"index {c.Index} id {c.Id} name {c.Name} short {c.ShortName}");
        return 0;
    }
    if (a.Length > 2 && a[1] == "managers")
    {
        foreach (var s in session.Staff.LoadAll().Where(s => s.JobCode == 16 && s.Club?.DisplayName == a[2]))
            Console.WriteLine($"{s.DisplayName} team '{s.TeamName}' rep {s.CurrentReputation} CA {s.CurrentAbility} kind {s.Kind} person 0x{s.Address:X} staff 0x{s.StaffAddress:X}");
        return 0;
    }
    if (a.Length > 2 && a[1] == "person")
    {
        uint want = uint.Parse(a[2]);
        foreach (var p in session.Players.LoadAll()) if (store.PersonIndex(p.Address + 0x288) == want) Console.WriteLine($"player {p.DisplayName} id {p.Id}");
        foreach (var s in session.Staff.LoadAll()) if (store.PersonIndex(s.Address) == want) Console.WriteLine($"staff {s.DisplayName} id {s.Id}");
        return 0;
    }
    if (store.Read(owner).FirstOrDefault() is { } first)
    {
        var clubs = session.LoadClubs(session.Players.LoadAll());
        var byClub = clubs.FirstOrDefault(c => c.Address == first.Club);
        var byTeam = clubs.FirstOrDefault(c => c.Teams.Any(t => t.Address == first.Club));
        Console.WriteLine($"promise club pointer = club {byClub?.DisplayName ?? "-"}, team of {byTeam?.DisplayName ?? "-"}; player club {player.Club?.DisplayName} 0x{player.Club?.Address:X}");
    }
    if (a.Length > 1)
    {
        var list = store.Read(owner);
        var game = session.ReadGameDate() ?? throw new InvalidOperationException("no game date");
        switch (a[1])
        {
            case "add":
            {
                var template = list.FirstOrDefault() ?? throw new InvalidOperationException("needs an existing promise to copy the manager and club from");
                int p0 = a.Length > 3 ? int.Parse(a[3]) : -1, p1 = a.Length > 4 ? int.Parse(a[4]) : -1;
                var values = new Fm27Editor.Core.Players.PromiseValues(int.Parse(a[2]), game, game.AddDays(91), p0, p1);
                Console.WriteLine($"add type {values.TypeId} manager {Name(template.Manager)}");
                if (write) { store.Add(owner, template.Manager, template.Club, values, session.AllocateGameBlock); Show(); }
                break;
            }
            case "remove":
            {
                var promise = list[int.Parse(a[2])];
                Console.WriteLine($"remove [{a[2]}] 0x{promise.Address:X}");
                if (write) { store.Remove(owner, promise); Show(); }
                break;
            }
            case "set":
            {
                var promise = list[int.Parse(a[2])];
                int p0 = a.Length > 4 ? int.Parse(a[4]) : -1, p1 = a.Length > 5 ? int.Parse(a[5]) : -1;
                var values = new Fm27Editor.Core.Players.PromiseValues(int.Parse(a[3]), promise.Made ?? game, promise.Deadline ?? game.AddDays(91), p0, p1);
                if (write) { store.Update(owner, promise, values); Show(); }
                break;
            }
            case "fixperson":
            {
                var promise = list[int.Parse(a[2])];
                ulong want = owner.Address + 0x288;
                Console.WriteLine($"person 0x{promise.Person:X} -> 0x{want:X}");
                if (write) session.Memory.TryWrite(promise.Address + 0x20, BitConverter.GetBytes(want));
                break;
            }
            case "dates":
            {
                var promise = list[int.Parse(a[2])];
                var values = new Fm27Editor.Core.Players.PromiseValues(promise.TypeId, DateOnly.Parse(a[3]), DateOnly.Parse(a[4]), promise.Parameter0, promise.Parameter1);
                if (write) { store.Update(owner, promise, values); Show(); }
                break;
            }
        }
    }
    return 0;
}

// ptactics <player id> [--write]   a player's tactical understanding values (lowest per slider, Position/Role last); --write sets them all to 100%
int PlayerTacticsCommand(string[] rest)
{
    bool write = rest.Contains("--write", StringComparer.OrdinalIgnoreCase);
    using var session = Attach(writable: write);
    var player = session.Players.LoadAll().FirstOrDefault(p => p.Id.ToString() == rest[0]) ?? throw new InvalidOperationException($"no player {rest[0]}");
    var tactics = session.PlayerTactics;
    string Show() => tactics.Read(player) is { } v ? string.Join(", ", v.Sliders.Select((s, i) => $"{Fm27Editor.Core.Players.PlayerTacticalUnderstanding.AreaNames[i]} {s?.ToString() ?? "-"}")) + $", {Fm27Editor.Core.Players.PlayerTacticalUnderstanding.AreaNames[6]} {v.PositionRole?.ToString() ?? "-"}" : "unavailable";
    Console.WriteLine($"{player.DisplayName} at 0x{player.Address:X}: {Show()}");
    if (rest.Contains("--cells")) foreach (var c in tactics.Cells(player)) Console.WriteLine($"  slider {c.Slider} 0x{c.At:X} size {c.Size} raw 0x{c.Raw:X}");
    if (write) { Console.WriteLine($"changed {tactics.Maximise(player)} value(s)"); Console.WriteLine($"now {Show()}"); }
    return 0;
}

// squadtactics <club name> [team index]   per area over a squad: averages of several candidate definitions of the club's Tactical Understanding bar (read only)
int SquadTacticsCommand(string[] rest)
{
    using var session = Attach(writable: false);
    var club = session.LoadClubs(session.Players.LoadAll()).FirstOrDefault(c => c.DisplayName == rest[0] || c.Name == rest[0]) ?? throw new InvalidOperationException($"no club {rest[0]}");
    var teams = club.Teams.OrderByDescending(t => t.IsFirstTeam).ToList();
    var team = teams[rest.Length > 1 ? int.Parse(rest[1]) : 0];
    var squad = session.ReadSquad(team.Address);
    var tactics = session.PlayerTactics;
    var names = Fm27Editor.Core.Players.PlayerTacticalUnderstanding.AreaNames;
    var perArea = Enumerable.Range(0, 7).Select(_ => new List<(int Min, int Max, double Mean, int Count)>()).ToList();
    int noRecord = 0;
    foreach (var p in squad)
    {
        var cells = tactics.Cells(p);
        if (cells.Count == 0) { noRecord++; continue; }
        for (int a = 0; a < 7; a++)
        {
            var vals = cells.Where(c => c.Slider == a).Select(c => (c.Size == 4 ? c.Raw & 0x3FFF : c.Raw)).ToList();
            if (vals.Count > 0) perArea[a].Add((vals.Min(), vals.Max(), vals.Average(), vals.Count));
        }
    }
    Console.WriteLine($"{club.DisplayName} team {team.Address:X} squad {squad.Count}, without record {noRecord}");
    // Candidate: team understanding of setting k = average over the squad of FM's level_for(items, k): max item level minus 1000 per step of distance to k, floor 1000.
    for (int a = 0; a < 6; a++)
    {
        var line = new List<string>();
        for (int k = 0; k <= 20; k++)
        {
            double sum = 0; int n = 0;
            foreach (var p in squad)
            {
                var items = tactics.Cells(p).Where(c => c.Slider == a && c.Size == 4).Select(c => c.Raw).ToList();
                if (items.Count == 0) continue;
                int best = 1000;
                foreach (int raw in items) best = Math.Max(best, (raw & 0x3FFF) - 1000 * Math.Abs(((raw >> 14) & 0x3F) - k));
                sum += Math.Min(best, 10000); n++;
            }
            line.Add($"{k}:{sum / n / 100:0}");
        }
        Console.WriteLine($"{names[a],-20} {string.Join(" ", line)}");
    }
    for (int a = 0; a < 7; a++)
    {
        var l = perArea[a];
        if (l.Count == 0) { Console.WriteLine($"{names[a],-42} -"); continue; }
        Console.WriteLine($"{names[a],-42} n={l.Count,2} avgMin {l.Average(x => x.Min) / 100:0.0} avgMax {l.Average(x => x.Max) / 100:0.0} avgMean {l.Average(x => x.Mean) / 100:0.0} avgCount {l.Average(x => x.Count):0.0}");
    }
    return 0;
}

// playerptr <player id> <lo hex> <hi hex> [depth]   offsets (and chains through pointed objects) whose qword lies in [lo, hi) (read only)
int PlayerPtrCommand(string[] rest)
{
    using var session = Attach(writable: false);
    var player = session.Players.LoadAll().FirstOrDefault(p => p.Id.ToString() == rest[0]) ?? throw new InvalidOperationException($"no player {rest[0]}");
    ulong lo = Convert.ToUInt64(rest[1].Replace("0x", ""), 16), hi = Convert.ToUInt64(rest[2].Replace("0x", ""), 16);
    int depth = rest.Length > 3 ? int.Parse(rest[3]) : 2;
    Console.WriteLine($"player {player.DisplayName} at 0x{player.Address:X}");
    var mem = session.Memory;
    var seen = new HashSet<ulong> { player.Address };
    var queue = new Queue<(ulong Addr, string Path, int D)>();
    queue.Enqueue((player.Address, "player", 0));
    int found = 0;
    while (queue.Count > 0 && found < 30)
    {
        var (addr, path, d) = queue.Dequeue();
        var b = new byte[0x400];
        if (!mem.TryRead(addr, b)) continue;
        for (int i = 0; i + 8 <= b.Length; i += 8)
        {
            ulong v = BitConverter.ToUInt64(b, i);
            if (v >= lo && v < hi) { Console.WriteLine($"{path}+0x{i:X} = 0x{v:X} (object 0x{addr:X}, so +0x{v - lo:X} past lo)"); found++; }
            if (v < 0x10000 || v > 0x7FFFFFFFFFFF || (v & 7) != 0) continue;
            if (d < depth && seen.Add(v)) queue.Enqueue((v, $"{path}+0x{i:X}->", d + 1));
        }
    }
    return 0;
}

// modptr <lo hex> <hi hex>   qwords in the game module's data that point into [lo, hi) (read only)
int ModPtrCommand(string[] rest)
{
    using var session = Attach(writable: false);
    ulong lo = Convert.ToUInt64(rest[0].Replace("0x", ""), 16), hi = Convert.ToUInt64(rest[1].Replace("0x", ""), 16);
    var mem = session.Memory;
    ulong b = session.Game.ModuleBase;
    foreach (var region in session.Process.EnumerateWritableRegions(b, session.Game.ModuleEnd).Where(r => r.IsModuleData))
    {
        for (ulong off = 0; off < region.Size; off += 0x10000)
        {
            int n = (int)Math.Min(0x10000UL, region.Size - off);
            var buf = new byte[n];
            if (!mem.TryRead(region.Start + off, buf)) continue;
            for (int i = 0; i + 8 <= n; i += 8)
            {
                ulong v = BitConverter.ToUInt64(buf, i);
                if (v >= lo && v < hi) Console.WriteLine($"module+0x{region.Start + off + (ulong)i - b:X} = 0x{v:X}");
            }
        }
    }
    return 0;
}

// ptrpath <player id> <target hex> [depth]   chains of pointers from the player object to a pointer array holding the target (read only)
int PtrPathCommand(string[] rest)
{
    using var session = Attach(writable: false);
    var player = session.Players.LoadAll().FirstOrDefault(p => p.Id.ToString() == rest[0]) ?? throw new InvalidOperationException($"no player {rest[0]}");
    ulong target = Convert.ToUInt64(rest[1].Replace("0x", ""), 16);
    int depth = rest.Length > 2 ? int.Parse(rest[2]) : 3;
    var mem = session.Memory;
    var seen = new HashSet<ulong> { player.Address };
    var queue = new Queue<(ulong Addr, string Path, int D)>();
    queue.Enqueue((player.Address, "player", 0));
    int found = 0;
    while (queue.Count > 0 && found < 20)
    {
        var (addr, path, d) = queue.Dequeue();
        var b = new byte[0x400];
        if (!mem.TryRead(addr, b)) continue;
        for (int i = 0; i + 8 <= b.Length; i += 8)
        {
            ulong v = BitConverter.ToUInt64(b, i);
            if (v == target) { Console.WriteLine($"{path}+0x{i:X} = target (object 0x{addr:X})"); found++; }
            if (v < 0x10000 || v > 0x7FFFFFFFFFFF || (v & 7) != 0) continue;
            // a vector begin/end pair: look for the target among its elements
            if (i + 16 <= b.Length)
            {
                ulong end = BitConverter.ToUInt64(b, i + 8);
                if (end > v && end - v <= 0x2000 && (end - v) % 8 == 0)
                {
                    var arr = new byte[end - v];
                    if (mem.TryRead(v, arr))
                        for (int k = 0; k < arr.Length; k += 8)
                            if (BitConverter.ToUInt64(arr, k) == target) { Console.WriteLine($"{path}+0x{i:X} vector[{k / 8}] of {arr.Length / 8} (object 0x{addr:X})"); found++; }
                }
            }
            if (d < depth && seen.Add(v)) queue.Enqueue((v, $"{path}+0x{i:X}->", d + 1));
        }
    }
    return 0;
}

// cityfield <city id> <latitude|longitude|altitude|inhabitants|attraction> <value> [--write]   change one single-value field of a city
int CityFieldCommand(string[] rest)
{
    bool write = rest.Contains("--write", StringComparer.OrdinalIgnoreCase);
    var a = rest.Where(x => !x.StartsWith("--")).ToArray();
    using var session = Attach(writable: write);
    var city = session.Cities.LoadAll().FirstOrDefault(c => c.Id.ToString() == a[0]) ?? throw new InvalidOperationException($"no city {a[0]}");
    var field = Enum.Parse<Fm27Editor.Core.Players.CityField>(a[1], ignoreCase: true);
    double value = double.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture);
    Console.WriteLine($"{city.Name}: lat {city.Latitude} lon {city.Longitude} alt {city.Altitude} inh {city.Inhabitants} attr {city.Attraction}; {field} -> {value}");
    if (write)
    {
        session.CityWriter.Set(city, field, value);
        var after = session.Cities.Read(city.Address)!;
        Console.WriteLine($"now lat {after.Latitude} lon {after.Longitude} alt {after.Altitude} inh {after.Inhabitants} attr {after.Attraction}");
    }
    return 0;
}

// citylink <city id> <nation|weather|language|region> [<name to set>] [--write]   the choices and the current value of a city link
int CityLinkCommand(string[] rest)
{
    bool write = rest.Contains("--write", StringComparer.OrdinalIgnoreCase);
    var args2 = rest.Where(a => !a.StartsWith("--")).ToArray();
    using var session = Attach(writable: write);
    var cities = session.Cities.LoadAll();
    var city = cities.FirstOrDefault(c => c.Id.ToString() == args2[0]) ?? throw new InvalidOperationException($"no city {args2[0]}");
    var link = Enum.Parse<Fm27Editor.Core.Players.CityLink>(args2[1], ignoreCase: true);
    var links = session.CityLinks;
    ulong current = links.Pointer(city, link);
    Console.WriteLine($"{city.Name}: {link} at 0x{current:X} = {(link == Fm27Editor.Core.Players.CityLink.Nation ? session.Nations.LoadAll().FirstOrDefault(n => n.Address == current)?.DisplayName : links.NameOf(current))}");
    if (link != Fm27Editor.Core.Players.CityLink.Nation)
    {
        var catalogue = links.Catalogue(cities, link);
        Console.WriteLine($"{catalogue.Count} choices: {string.Join(", ", catalogue.Take(40).Select(t => t.Name))}");
        if (args2.Length > 2)
        {
            var target = catalogue.FirstOrDefault(t => t.Name.Equals(args2[2], StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException($"no {link} named {args2[2]}");
            Console.WriteLine($"set -> 0x{target.Address:X} {target.Name}");
            if (write) { links.Set(city, link, current, target.Address, catalogue); Console.WriteLine($"now {links.NameOf(links.Pointer(city, link))}"); }
        }
    }
    else if (args2.Length > 2)
    {
        var target = session.Nations.LoadAll().FirstOrDefault(n => n.DisplayName.Equals(args2[2], StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException($"no nation {args2[2]}");
        Console.WriteLine($"set -> 0x{target.Address:X} {target.DisplayName}");
        if (write) { links.Set(city, link, current, target.Address); Console.WriteLine($"now 0x{links.Pointer(city, link):X}"); }
    }
    return 0;
}

// temporarypermits  players whose work permit is temporary (has a review date), with the record bytes (read only)
int TemporaryPermitsCommand()
{
    using var session = Attach(writable: false);
    var table = new Fm27Editor.Core.Players.WorkPermitTable(session.Memory, session.Profile, session.Game.ModuleBase);
    var byPerson = session.Players.LoadAll().GroupBy(p => BitConverter.ToUInt32(session.Memory.Read(p.Address + 0x290, 4))).ToDictionary(g => g.Key, g => g.First());
    ulong at = table.Locate();
    ulong b = session.Memory.ReadUInt64(at), e = session.Memory.ReadUInt64(at + 8);
    var pointers = session.Memory.Read(b, (int)(e - b));
    int shown = 0;
    for (int i = 0; i < pointers.Length && shown < 12; i += 8)
    {
        var record = session.Memory.Read(BitConverter.ToUInt64(pointers, i), 0x20);
        if (BitConverter.ToUInt16(record, 0x16) != 1) continue;
        if (byPerson.TryGetValue(BitConverter.ToUInt32(record, 8), out var player))
        { Console.WriteLine($"{player.Id} {player.DisplayName,-24} record 0x{BitConverter.ToUInt64(pointers, i):X} {Convert.ToHexString(record)}"); shown++; }
    }
    return 0;
}

// permits <player id> [add <nation> <year> | remove | year <year>] [--write]   the work permits given to a player (read only unless --write)
int PermitsCommand(string[] rest)
{
    bool write = rest.Contains("--write", StringComparer.OrdinalIgnoreCase);
    using var session = Attach(writable: write);
    var player = session.Players.LoadAll().FirstOrDefault(p => p.Id.ToString() == rest[0]) ?? throw new InvalidOperationException($"no player {rest[0]}");
    var before = session.ReadWorkPermits(player);
    Console.WriteLine($"{player.DisplayName}: {before.Count} permit(s)");
    foreach (var g in before) Console.WriteLine($"  {g.NationName} {g.Year} review {g.ReviewFrom?.ToString("yyyy-MM-dd") ?? "-"} temporary {g.Temporary} record 0x{g.Record:X}");
    if (rest.Length > 1 && write)
    {
        if (rest[1] == "add")
        {
            var nation = session.Nations.LoadAll().First(n => n.DisplayName.Equals(rest[2], StringComparison.OrdinalIgnoreCase));
            session.AddWorkPermit(player, nation.Address, int.Parse(rest[3]));
        }
        else if (rest[1] == "remove") session.RemoveWorkPermit(player, before[0]);
        else if (rest[1] == "year") session.SetWorkPermitYear(player, before[0], int.Parse(rest[2]));
        else if (rest[1] == "permanent") session.MakeWorkPermitPermanent(player, before.First(g => g.Temporary));
        var after = session.ReadWorkPermits(player);
        Console.WriteLine($"now {after.Count} permit(s)");
        foreach (var g in after) Console.WriteLine($"  {g.NationName} {g.Year} record 0x{g.Record:X}");
        Console.WriteLine($"flag byte at player+0x2A0: 0x{session.Memory.Read(player.Address + 0x2A0, 1)[0]:X2}");
    }
    return 0;
}

// permitbatch <club name> <nation> [--write]   grant a work permit from <nation> to the club's first-team squad, then read every permit back
int ForceAcceptCommand(string[] rest)
{
    string action = rest.Length > 0 ? rest[0].ToLowerInvariant() : "plan";
    string presetName = rest.Length > 1 ? rest[1].ToLowerInvariant() : "transfer";
    var preset = presetName switch
    {
        "board" => ForceAcceptPreset.BoardRequests,
        "contract" => ForceAcceptPreset.ContractOffers,
        "transfer" => ForceAcceptPreset.TransferOffers,
        _ => Enum.Parse<ForceAcceptPreset>(presetName, true),
    };
    bool write = rest.Contains("--write", StringComparer.OrdinalIgnoreCase);
    bool paused = rest.Contains("--paused", StringComparer.OrdinalIgnoreCase);
    using var session = Attach(writable: write);
    var planner = new ForceAcceptPlanner(session.Profile, session.Memory, session.Game.ModuleBase);
    var plan = planner.Plan(preset);
    Console.WriteLine($"{preset}: {plan.State}");
    foreach (var site in plan.Sites)
        Console.WriteLine($"  {site.Key}: {site.State} rva={(site.Rva is { } r ? $"0x{r:X}" : "-")} current={(site.Current is null ? "-" : Convert.ToHexString(site.Current))}");
    if (action == "plan") return 0;
    if (action is not ("apply" or "restore"))
    {
        Console.WriteLine("usage: forceaccept plan|apply|restore [board|contract|transfer] [--write --paused]");
        return 2;
    }
    if (!write || !paused)
    {
        Console.WriteLine("apply/restore need --write --paused (only while the game is paused)");
        return 2;
    }
    int changed = action == "apply" ? planner.Apply(plan, gamePaused: true) : planner.Restore(plan, gamePaused: true);
    Console.WriteLine($"{action}: {changed} site(s)");
    var after = planner.Plan(preset);
    Console.WriteLine($"{preset}: {after.State}");
    foreach (var site in after.Sites)
        Console.WriteLine($"  {site.Key}: {site.State} current={(site.Current is null ? "-" : Convert.ToHexString(site.Current))}");
    return 0;
}

int ForceAcceptHookCommand(string[] rest)
{
    string action = rest.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))?.ToLowerInvariant() ?? "status";
    bool write = rest.Contains("--write", StringComparer.OrdinalIgnoreCase);
    bool mutating = action is "install" or "enable" or "disable" or "uninstall";
    using var session = Attach(writable: mutating && write);
    var hook = session.ForceTransferOffersHook;
    ulong site = hook.FindSite();
    var bytes = session.Memory.Read(site, Fm27Editor.Core.Presets.ForceTransferOffersHook.OriginalPattern().Length);

    List<int> ManagedIds()
    {
        if (session.Profile.Club?.Id is not { } idOff) return [];
        var ids = new List<int>();
        foreach (var s in session.Staff.LoadAll())
        {
            if (s.ManagerAttributes is null || s.Club is not { } club) continue;
            var raw = new byte[4];
            if (!session.Memory.TryRead(club.Address + (ulong)idOff, raw)) continue;
            int id = BitConverter.ToInt32(raw, 0);
            if (id != 0 && id != -1 && !ids.Contains(id)) ids.Add(id);
        }
        return ids;
    }

    void PrintState(Fm27Editor.Core.Presets.ForceTransferOffersState? s)
    {
        if (s is null)
        {
            Console.WriteLine($"site=0x{site:X} (RVA 0x{site - session.Game.ModuleBase:X}) state=Original bytes={Convert.ToHexString(bytes)}");
            return;
        }
        Console.WriteLine($"site=0x{s.Site:X} (RVA 0x{s.Site - session.Game.ModuleBase:X}) cave=0x{s.Cave:X} enabled={s.Enabled} calls={s.Calls} forced={s.Forced} clubIds=[{string.Join(",", s.ClubIds)}]");
        foreach (var h in s.Recent.Take(10))
            Console.WriteLine($"  proposal=0x{h.Proposal:X} club=0x{h.BuyerClub:X} uid={h.BuyerClubId} orig={h.OriginalStatus} new={h.NewStatus} forced={h.Forced}");
    }

    if (action is "status" or "plan")
    {
        PrintState(hook.Read());
        Console.WriteLine($"managedClubIds=[{string.Join(",", ManagedIds())}]");
        return 0;
    }

    if (!mutating) throw new InvalidOperationException("usage: forceaccept-hook status|plan|install|enable [clubId...]|disable|uninstall [--write]");
    if (!write)
    {
        Console.WriteLine($"plan only for '{action}' at site=0x{site:X} (RVA 0x{site - session.Game.ModuleBase:X}); add --write to apply");
        PrintState(hook.Read());
        return 0;
    }

    switch (action)
    {
        case "install":
            PrintState(hook.Install());
            break;
        case "enable":
        {
            var explicitIds = rest.Skip(1).Where(a => !a.StartsWith("--", StringComparison.Ordinal)).Select(int.Parse).ToList();
            var ids = explicitIds.Count > 0 ? explicitIds : ManagedIds();
            if (ids.Count == 0) throw new InvalidOperationException("no managed club ids found; pass explicit club id(s)");
            PrintState(hook.Configure(true, ids));
            break;
        }
        case "disable":
        {
            var existing = hook.Read();
            var ids = existing?.ClubIds ?? ManagedIds();
            PrintState(existing is null ? hook.Install() : hook.Configure(false, ids));
            break;
        }
        case "uninstall":
            hook.Uninstall();
            var restored = session.Memory.Read(site, Fm27Editor.Core.Presets.ForceTransferOffersHook.OriginalPattern().Length);
            Console.WriteLine($"uninstalled: site=0x{site:X} bytes={Convert.ToHexString(restored)} read={hook.Read()?.ToString() ?? "null"}");
            break;
    }
    return 0;
}

int ForceAcceptDealCommand(string[] rest)
{
    bool write = rest.Contains("--write", StringComparer.OrdinalIgnoreCase);
    string key = rest.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))
        ?? throw new InvalidOperationException("usage: forceacceptdeal <player id|name> [--write]");
    using var session = Attach(writable: write);
    var matches = session.Players.LoadAll()
        .Where(p => p.Id?.ToString() == key || string.Equals(p.DisplayName, key, StringComparison.OrdinalIgnoreCase))
        .Take(2).ToList();
    if (matches.Count != 1) throw new InvalidOperationException(matches.Count == 0 ? $"no player '{key}'" : $"player name '{key}' is ambiguous");
    var player = matches[0];
    var deal = session.TransferDeals.FindForPlayer(session.PersonOf(player))
        ?? throw new InvalidOperationException($"{player.DisplayName} has no transfer deal");
    Console.WriteLine($"{player.DisplayName}: deal 0x{deal.Address:X} status=0x{deal.StatusRaw:X} ({deal.Status}) fee={deal.Fee} " +
        $"{session.TransferDeals.TeamName(deal.BuyingTeam)} <- {session.TransferDeals.TeamName(deal.SellingTeam)}");
    if (!write)
    {
        Console.WriteLine("plan only; add --write to accept this deal (data write, no code patch)");
        return 0;
    }
    if (!deal.IsOpen)
        Console.WriteLine($"warning: deal is {deal.Status}; writing the accepted status anyway");
    session.TransferDealWriter.AcceptOffer(deal);
    var after = session.TransferDeals.Read(deal.Address) ?? throw new InvalidOperationException("deal disappeared");
    Console.WriteLine($"after: status=0x{after.StatusRaw:X} ({after.Status}) accepted={after.IsAccepted}");
    return 0;
}

int ForceAcceptOfferCommand(string[] rest)
{
    bool write = rest.Contains("--write", StringComparer.OrdinalIgnoreCase);
    string key = rest.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))
        ?? throw new InvalidOperationException("usage: forceacceptoffer <player id|name> [offer id] [--write]");
    int? offerId = rest.Skip(1).Select(a => int.TryParse(a, out int n) ? n : (int?)null).FirstOrDefault(n => n is not null);
    using var session = Attach(writable: write);
    var matches = session.Players.LoadAll()
        .Where(p => p.Id?.ToString() == key || string.Equals(p.DisplayName, key, StringComparison.OrdinalIgnoreCase))
        .Take(2).ToList();
    if (matches.Count != 1) throw new InvalidOperationException(matches.Count == 0 ? $"no player '{key}'" : $"player name '{key}' is ambiguous");
    var player = matches[0];
    var offers = session.Proposals.Read(player).Where(o => o.Type == ProposalType.Transfer).ToList();
    var offer = offerId is { } id ? offers.FirstOrDefault(o => o.Id == id) : offers.FirstOrDefault();
    if (offer is null) throw new InvalidOperationException($"{player.DisplayName} has no transfer offer{(offerId is { } x ? $" #{x}" : "")}");
    Console.WriteLine($"{player.DisplayName}: offer #{offer.Id} {offer.From} -> {offer.To} fee={offer.Value} status={offer.Status}");
    if (!write)
    {
        Console.WriteLine("plan only; add --write to set seller accepted (proposal status 5; data, no code patch)");
        return 0;
    }
    session.AcceptTransferOffer(player, offer);
    var after = session.Proposals.ReadOffer(offer.Address) ?? throw new InvalidOperationException("offer disappeared");
    Console.WriteLine($"after: offer status={after.Status} (5 = seller accepted)");
    return 0;
}

int PermitBatchCommand(string[] rest)
{
    bool write = rest.Contains("--write", StringComparer.OrdinalIgnoreCase);
    var words = rest.Where(a => !a.Equals("--write", StringComparison.OrdinalIgnoreCase)).ToArray();
    using var session = Attach(writable: write);
    var all = session.Players.LoadAll();
    var club = session.LoadClubs(all.ToArray()).FirstOrDefault(c => c.DisplayName.Equals(words[0], StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"no club '{words[0]}'");
    var nation = session.Nations.LoadAll().First(n => n.DisplayName.Equals(words[1], StringComparison.OrdinalIgnoreCase));
    var squad = club.FirstTeam is { } team ? session.ReadSquad(team.Address) : [];
    int year = session.ReadGameDate()?.Year ?? throw new InvalidOperationException("game date not readable");
    int Holding() => squad.Count(p => session.ReadWorkPermits(p).Any(g => g.Nation == nation.Address));
    Console.WriteLine($"{club.DisplayName}: {squad.Count} squad players; {Holding()} already hold a {nation.DisplayName} permit; year {year}");
    if (!write) { Console.WriteLine("plan only; add --write"); return 0; }
    var skips = new List<WorkPermitBatchSkip>();
    var sw = Stopwatch.StartNew();
    int granted = session.BatchGrantWorkPermits(squad, nation.Address, year, a => a == nation.Address ? nation.DisplayName : null, skips.Add);
    Console.WriteLine($"granted {granted}, skipped {skips.Count} in {sw.ElapsedMilliseconds} ms");
    foreach (var s in skips.Take(10)) Console.WriteLine($"  skip {s.PlayerName}: {s.Reason}");
    Console.WriteLine($"now {Holding()} of {squad.Count} hold a {nation.DisplayName} permit");
    return 0;
}

// workpermit <player id> [remove] [--write]   the clubs refused a work permit for the player (read only unless remove --write)
int WorkPermitCommand(string[] rest)
{
    bool write = rest.Contains("--write", StringComparer.OrdinalIgnoreCase);
    using var session = Attach(writable: write);
    var player = session.Players.LoadAll().FirstOrDefault(p => p.Id.ToString() == rest[0]) ?? throw new InvalidOperationException($"no player {rest[0]}");
    var denied = session.ReadWorkPermitDenied(player);
    Console.WriteLine($"{player.DisplayName}: {denied.Count} club(s) refused a work permit");
    foreach (var d in denied) Console.WriteLine($"  {d.ClubName} (team 0x{d.Team:X})");
    if (rest.Contains("remove", StringComparer.OrdinalIgnoreCase) && write && denied.Count > 0)
    {
        Console.WriteLine($"removed: {session.RemoveWorkPermitDenied(player, denied[0])}");
        Console.WriteLine($"now {session.ReadWorkPermitDenied(player).Count}");
    }
    return 0;
}

// playerslot <player id>   the player's position in the game's player pointer array (the index work-permit records use)
int PlayerSlotCommand(string key, string? findHex = null)
{
    using var session = Attach(writable: false);
    var player = session.Players.LoadAll().FirstOrDefault(p => p.Id.ToString() == key) ?? throw new InvalidOperationException($"no player {key}");
    if (findHex is not null)
    {
        var needle = Convert.FromHexString(findHex);
        var obj = session.Memory.Read(player.Address, session.Profile.PlayerObjectSize + 0x100);
        Console.WriteLine($"{player.DisplayName} at 0x{player.Address:X}");
        for (int k = 0; k + needle.Length <= obj.Length; k++) if (obj.AsSpan(k, needle.Length).SequenceEqual(needle)) Console.WriteLine($"  +0x{k:X}");
        return 0;
    }
    ulong root = session.Game.ModuleBase + session.Profile.PlayerListRootRva;
    ulong begin = session.Memory.ReadUInt64(root + (ulong)session.Profile.PlayerListBeginOffset), end = session.Memory.ReadUInt64(root + (ulong)session.Profile.PlayerListEndOffset);
    var raw = session.Memory.Read(begin, (int)(end - begin));
    for (int i = 0; i < raw.Length / 8; i++)
        if (BitConverter.ToUInt64(raw, i * 8) == player.Address) { Console.WriteLine($"{player.DisplayName}: slot {i} of {raw.Length / 8}"); return 0; }
    Console.WriteLine("not in the array");
    return 1;
}

// permitscan [flag]   heap records of the 0x20-byte family (nation pointer at +0, year at +0x10, flag u16 at +0x16) whose flag equals <flag>
int PermitScanCommand(int flag)
{
    using var session = Attach(writable: false);
    var nations = session.Nations.LoadAll().ToDictionary(n => n.Address, n => n.DisplayName);
    var everyone = session.Players.LoadAll();
    var buffer = new byte[1 << 22];
    int hits = 0, family = 0;
    foreach (var region in session.Process.EnumerateWritableRegions(session.Game.ModuleBase, session.Game.ModuleEnd).Where(r => !r.IsModuleData))
        for (ulong offset = 0; offset < region.Size; offset += (ulong)buffer.Length - 64)
        {
            int length = (int)Math.Min((ulong)buffer.Length, region.Size - offset);
            if (!session.Memory.TryRead(region.Start + offset, buffer.AsSpan(0, length))) break;
            for (int i = 0; i + 0x20 <= length; i += 8)
            {
                if (!nations.TryGetValue(BitConverter.ToUInt64(buffer, i), out var nation)) continue;
                int year = BitConverter.ToUInt16(buffer, i + 0x10);
                if (year is < 1980 or > 2060) continue;
                family++;
                if (BitConverter.ToUInt16(buffer, i + 0x16) != flag) continue;
                if (++hits <= 400)
                    Console.WriteLine($"0x{region.Start + offset + (ulong)i:X} {nation,-16} {Convert.ToHexString(buffer, i + 8, 24)} idx {BitConverter.ToUInt32(buffer, i + 8)} -> {(BitConverter.ToUInt32(buffer, i + 8) < everyone.Count ? everyone[(int)BitConverter.ToUInt32(buffer, i + 8)].DisplayName + " (" + everyone[(int)BitConverter.ToUInt32(buffer, i + 8)].Nationality?.DisplayName + ")" : "?")}");
            }
        }
    Console.WriteLine($"{family} records of the family, {hits} with flag {flag}");
    return 0;
}

// memdump <addrHex> [lenHex]   bytes of the game process at an address (read only)
int MemDumpCommand(string address, int length)
{
    using var session = Attach(writable: false);
    ulong at = Convert.ToUInt64(address.Replace("0x", ""), 16);
    var bytes = session.Memory.Read(at, length);
    for (int i = 0; i < bytes.Length; i += 16) Console.WriteLine($"0x{at + (ulong)i:X}  {Convert.ToHexString(bytes.AsSpan(i, Math.Min(16, bytes.Length - i)))}");
    return 0;
}

// heapfind <hex with ?? wildcards> [<hex> ...] [--near N]   heap places where the first pattern matches and every other pattern occurs within N bytes (default 48)
int HeapFindCommand(string[] rest)
{
    int near = 48;
    var patterns = new List<string>();
    for (int i = 0; i < rest.Length; i++)
        if (rest[i] == "--near" && i + 1 < rest.Length) near = int.Parse(rest[++i]); else patterns.Add(rest[i]);
    static (byte[] Bytes, bool[] Mask) Parse(string hex) => (Enumerable.Range(0, hex.Length / 2).Select(k => hex.Substring(k * 2, 2) == "??" ? (byte)0 : Convert.ToByte(hex.Substring(k * 2, 2), 16)).ToArray(),
        Enumerable.Range(0, hex.Length / 2).Select(k => hex.Substring(k * 2, 2) != "??").ToArray());
    static bool At(byte[] b, int at, (byte[] Bytes, bool[] Mask) p)
    {
        if (at < 0 || at + p.Bytes.Length > b.Length) return false;
        for (int k = 0; k < p.Bytes.Length; k++) if (p.Mask[k] && b[at + k] != p.Bytes[k]) return false;
        return true;
    }
    using var session = Attach(writable: false);
    // "nation:England" stands for the 8 bytes of that nation object's address.
    patterns = patterns.Select(pt => pt.StartsWith("nation:") ? Convert.ToHexString(BitConverter.GetBytes(session.Nations.LoadAll().First(n => n.DisplayName.Equals(pt[7..], StringComparison.OrdinalIgnoreCase)).Address)) : pt).ToList();
    var parsed = patterns.Select(Parse).ToList();
    var buffer = new byte[1 << 22];
    int hits = 0;
    foreach (var region in session.Process.EnumerateWritableRegions(session.Game.ModuleBase, session.Game.ModuleEnd).Where(r => Environment.GetEnvironmentVariable("HEAPFIND_MODULE") == "1" || !r.IsModuleData))
    {
        for (ulong offset = 0; offset < region.Size; offset += (ulong)buffer.Length - 256)
        {
            int length = (int)Math.Min((ulong)buffer.Length, region.Size - offset);
            if (!session.Memory.TryRead(region.Start + offset, buffer.AsSpan(0, length))) break;
            var b = length == buffer.Length ? buffer : buffer[..length];
            for (int i = 0; i + parsed[0].Bytes.Length <= length; i++)
            {
                if (!At(b, i, parsed[0])) continue;
                bool all = true;
                foreach (var extra in parsed.Skip(1))
                {
                    bool found = false;
                    for (int d = -near; d <= near && !found; d++) found = At(b, i + d, extra);
                    if (!found) { all = false; break; }
                }
                if (!all) continue;
                int from = Math.Max(0, i - 32);
                Console.WriteLine($"0x{region.Start + offset + (ulong)i:X}: " + Convert.ToHexString(b, from, Math.Min(96, length - from)));
                if (++hits >= 30) { Console.WriteLine("(30 hits, stopping)"); return 0; }
            }
        }
    }
    Console.WriteLine($"{hits} hit(s)");
    return 0;
}

// reldump <player id|name>   the relationship rows of a player, 16 bytes each (read only)
int RelDumpCommand(string key)
{
    using var session = Attach(writable: false);
    var player = session.Players.LoadAll().FirstOrDefault(p => p.Id.ToString() == key) ?? session.Players.LoadAll().FirstOrDefault(p => p.DisplayName.Equals(key, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"no player '{key}'");
    var o = session.Profile.Relations!;
    ulong holder = session.Memory.ReadUInt64(player.Address + (ulong)o.Pointer);
    ulong begin = session.Memory.ReadUInt64(holder), end = session.Memory.ReadUInt64(holder + 8);
    Console.WriteLine($"{player.DisplayName} id {player.Id}: holder 0x{holder:X} vector 0x{begin:X}..0x{end:X} ({(end - begin) / 16} rows)");
    var bytes = session.Memory.Read(begin, (int)(end - begin));
    for (int k = 0; k + 16 <= bytes.Length; k += 16)
        Console.WriteLine($"  [{k / 16}] type 0x{bytes[k + o.Type]:X2} value {(sbyte)bytes[k + o.Code]} {Convert.ToHexString(bytes, k, 16)}");
    return 0;
}

// intwithdrawal <player id|name> [--write]   rewrite the player's International Withdrawal rows as FMRTE's Remove Int. Withdrawal does
int IntWithdrawalCommand(string key)
{
    bool write = key.Contains("--write");
    key = key.Replace("--write", "").Trim();
    using var session = Attach(writable: write);
    var player = session.Players.LoadAll().FirstOrDefault(p => p.Id.ToString() == key) ?? session.Players.LoadAll().FirstOrDefault(p => p.DisplayName.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException($"no player '{key}'");
    if (!write) { Console.WriteLine("dry run; pass --write"); return 0; }
    Console.WriteLine($"{player.DisplayName}: {session.RemoveInternationalWithdrawal(player)} row(s) rewritten");
    return 0;
}

// relstats   how many relationship rows of each type exist in the game (read only)
int RelStatsCommand()
{
    using var session = Attach(writable: false);
    var o = session.Profile.Relations!;
    var counts = new SortedDictionary<int, int>();
    foreach (var p in session.Players.LoadAll())
    {
        ulong holder = session.Memory.ReadUInt64(p.Address + (ulong)o.Pointer);
        if (holder == 0) continue;
        ulong begin = session.Memory.ReadUInt64(holder), end = session.Memory.ReadUInt64(holder + 8);
        if (begin == 0 || end < begin || end - begin > 500 * 16) continue;
        var bytes = session.Memory.Read(begin, (int)(end - begin));
        for (int k = 0; k + 16 <= bytes.Length; k += 16) counts[bytes[k + o.Type]] = counts.GetValueOrDefault(bytes[k + o.Type]) + 1;
    }
    foreach (var (type, n) in counts) Console.WriteLine($"  0x{type:X2} {n}");
    return 0;
}

// reltype <type hex> [max]   sample relation rows of one type: owner, target object kind and name, raw 16 bytes (read only)
int RelTypeSampleCommand(string[] rest)
{
    using var session = Attach(writable: false);
    var o = session.Profile.Relations!;
    int want = Convert.ToInt32(rest[0].Replace("0x", ""), 16), max = rest.Length > 1 ? int.Parse(rest[1]) : 25, shown = 0;
    var clubs = session.LoadClubs(session.Players.LoadAll());
    var nameOf = new Dictionary<ulong, string>();
    foreach (var c in clubs) { nameOf[c.Address] = "club " + c.DisplayName; foreach (var t in c.Teams) nameOf[t.Address] = "team of " + c.DisplayName; }
    var values = new Dictionary<int, int>();
    foreach (var p in session.Players.LoadAll())
    {
        ulong holder = session.Memory.ReadUInt64(p.Address + (ulong)o.Pointer);
        if (holder == 0) continue;
        ulong begin = session.Memory.ReadUInt64(holder), end = session.Memory.ReadUInt64(holder + 8);
        if (begin == 0 || end < begin || end - begin > 500 * 16) continue;
        var bytes = session.Memory.Read(begin, (int)(end - begin));
        for (int k = 0; k + 16 <= bytes.Length; k += 16)
        {
            if (bytes[k + o.Type] != want) continue;
            values[bytes[k + 12]] = values.GetValueOrDefault(bytes[k + 12]) + 1;
            if (shown++ >= max) continue;
            ulong target = BitConverter.ToUInt64(bytes, k);
            Console.WriteLine($"{p.DisplayName,-24} ({p.ClubName,-18}) target {(nameOf.TryGetValue(target, out var n) ? n : $"0x{target:X}")}  {Convert.ToHexString(bytes.AsSpan(k, 16))}");
        }
    }
    Console.WriteLine("value histogram: " + string.Join(", ", values.OrderBy(v => v.Key).Select(v => $"{v.Key}:{v.Value}")));
    return 0;
}

// stadiumset <stadium id> owner <club id> | location <city id> [--write]   change a stadium's owner club or location city
int StadiumSetCommand(string[] rest)
{
    bool write = rest.Contains("--write", StringComparer.OrdinalIgnoreCase);
    using var session = Attach(writable: write);
    var stadium = session.Stadiums.LoadAll().FirstOrDefault(s => s.Id.ToString() == rest[0]) ?? throw new InvalidOperationException($"no stadium {rest[0]}");
    ulong cityNow = session.Stadiums.LocationPointer(stadium.Address);
    Console.WriteLine($"{stadium.Name}: owner {stadium.Owner?.DisplayName}, location {session.Stadiums.LocationOf(stadium.Address)?.Name} (city id {session.Stadiums.LocationOf(stadium.Address)?.Id})");
    if (rest[1].Equals("owner", StringComparison.OrdinalIgnoreCase))
    {
        var club = session.LoadClubs(session.Players.LoadAll()).FirstOrDefault(c => c.Id.ToString() == rest[2]) ?? throw new InvalidOperationException($"no club {rest[2]}");
        Console.WriteLine($"owner -> {club.DisplayName}");
        if (write) session.StadiumWriter.SetOwner(stadium, club.Address);
    }
    else if (rest[1].Equals("nearby", StringComparison.OrdinalIgnoreCase))
    {
        var other = session.Stadiums.LoadAll().FirstOrDefault(s => s.Id.ToString() == rest[2]) ?? throw new InvalidOperationException($"no stadium {rest[2]}");
        Console.WriteLine($"nearby {stadium.NearbyStadium} -> {other.Name}");
        if (write) session.StadiumWriter.SetNearby(stadium, session.Stadiums.NearbyPointer(stadium.Address), other.Address);
    }
    else
    {
        var city = session.Cities.LoadAll().FirstOrDefault(c => c.Id.ToString() == rest[2]) ?? throw new InvalidOperationException($"no city {rest[2]}");
        Console.WriteLine($"location -> {city.Name} (id {city.Id})");
        if (write) session.StadiumWriter.SetLocation(stadium, cityNow, city.Address);
    }
    var after = session.Stadiums.Reload(stadium.Address)!;
    Console.WriteLine($"now owner {after.Owner?.DisplayName}, location {session.Stadiums.LocationOf(stadium.Address)?.Name}{(write ? "" : " (dry run)")}");
    return 0;
}

// stadiumdump <stadium id> [length hex]   the stadium object bytes, and what its pointers lead to (read only)
int StadiumDumpCommand(string[] rest)
{
    using var session = Attach(writable: false);
    var stadium = session.Stadiums.LoadAll().FirstOrDefault(s => s.Id.ToString() == rest[0]) ?? throw new InvalidOperationException($"no stadium {rest[0]}");
    int length = rest.Length > 1 ? Convert.ToInt32(rest[1], 16) : 0x180;
    var bytes = session.Memory.Read(stadium.Address, length);
    Console.WriteLine($"{stadium.Name} id {stadium.Id} at 0x{stadium.Address:X}, owner {stadium.Owner?.DisplayName}");
    for (int i = 0; i < bytes.Length; i += 16) Console.WriteLine($"  +{i:X3} {Convert.ToHexString(bytes.AsSpan(i, 16))}");
    return 0;
}

// injuryadd <player id> <type id> [--write] | injuryadd <player id> remove [--write]   add an injury of a kind, or remove the player's last one
int InjuryAddCommand(string[] rest)
{
    bool write = rest.Contains("--write", StringComparer.OrdinalIgnoreCase);
    using var session = Attach(writable: write);
    var players = session.Players.LoadAll();
    var player = players.FirstOrDefault(p => p.Id.ToString() == rest[0]) ?? throw new InvalidOperationException($"no player {rest[0]}");
    if (rest[1].Equals("remove", StringComparison.OrdinalIgnoreCase))
    {
        var last = session.ReadInjuries(player).LastOrDefault() ?? throw new InvalidOperationException("no injury to remove");
        Console.WriteLine($"{player.DisplayName}: remove '{last.Name}'");
        if (write) { session.RemoveInjury(player, last); Console.WriteLine($"now {session.ReadInjuries(player).Count} injuries"); }
        return 0;
    }
    var template = session.InjuryTemplate(player, players) ?? throw new InvalidOperationException("no injury in the game to copy");
    var type = session.InjuryTypes(template).FirstOrDefault(t => t.Id.ToString() == rest[1]) ?? throw new InvalidOperationException($"no type {rest[1]}");
    Console.WriteLine($"{player.DisplayName}: add '{type.Name}' ({type.Id}) {type.DefaultMinimumDays}+{type.DefaultExtraDays}, had {session.ReadInjuries(player).Count} injuries");
    if (!write) { Console.WriteLine("dry run; pass --write"); return 0; }
    var made = session.AddInjury(player, template, type, Fm27Editor.Core.Players.InjuryTreatment.Physio);
    Console.WriteLine($"added 0x{made.Address:X} '{made.Name}' {made.Start} {made.MinimumDays}+{made.ExtraDays}; now {session.ReadInjuries(player).Count} injuries");
    return 0;
}

// injuryscan   which bytes of an injury record differ between all injured players (read only)
int InjuryScanCommand()
{
    using var session = Attach(writable: false);
    var all = new List<byte[]>();
    var types = new Dictionary<int, string>();
    foreach (var p in session.Players.LoadAll())
        foreach (var i in session.ReadInjuries(p)) { all.Add(Convert.FromHexString(i.Snapshot)); types[i.TypeId] = $"{i.Name} {i.MinimumDays}+{i.ExtraDays}"; }
    Console.WriteLine($"{all.Count} injury records, {types.Count} types in use");
    if (all.Count == 0) return 0;
    for (int b = 0; b < all[0].Length; b++)
    {
        var values = all.Select(r => r[b]).GroupBy(v => v).OrderByDescending(g => g.Count()).Take(6).Select(g => $"{g.Key:X2}x{g.Count()}");
        Console.WriteLine($"  +{b:X2}: {string.Join(" ", values)}");
    }
    return 0;
}

// injurydump <player id|name>   every injury of a player with its record bytes and the vector header (read only)
int InjuryDumpCommand(string[] rest)
{
    using var session = Attach(writable: false);
    string key = string.Join(" ", rest);
    var player = session.Players.LoadAll().FirstOrDefault(p => p.Id.ToString() == key) ?? session.Players.LoadAll().FirstOrDefault(p => p.DisplayName.Equals(key, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"no player '{key}'");
    var o = session.Profile.Injuries ?? throw new InvalidOperationException("no injury offsets");
    ulong holder = session.Memory.ReadUInt64(player.Address + (ulong)o.Pointer);
    Console.WriteLine($"{player.DisplayName} id {player.Id} at 0x{player.Address:X}: holder 0x{holder:X} (player+0x{o.Pointer:X}), record size 0x{o.RecordSize:X}");
    if (holder != 0)
        Console.WriteLine("  holder " + Convert.ToHexString(session.Memory.Read(holder, 0x20)));
    foreach (var i in session.ReadInjuries(player))
    {
        Console.WriteLine($"  0x{i.Address:X} type {i.TypeId} '{i.Name}' start {i.Start} {i.MinimumDays}+{i.ExtraDays} treatment {i.Treatment}");
        Console.WriteLine("    " + i.Snapshot);
    }
    return 0;
}

// injurytype list | injurytype <player id> <type id> [--write]   the injury kinds of the game; change the kind of a player's first injury
int InjuryTypeCommand(string[] rest)
{
    bool write = rest.Contains("--write", StringComparer.OrdinalIgnoreCase);
    using var session = Attach(writable: write);
    var sampleHolder = session.Players.LoadAll().Select(pl => (pl, injuries: session.ReadInjuries(pl))).FirstOrDefault(x => x.injuries.Count > 0);
    var types = session.InjuryTypes(sampleHolder.injuries?[0]);
    if (rest.Length == 0 || rest[0].Equals("list", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"{types.Count} injury types; sample injured player {sampleHolder.pl?.Id} {sampleHolder.pl?.DisplayName}");
        foreach (var t in types) Console.WriteLine($"  {t.Id,5} {t.Name} 0x{t.Address:X}");
        return 0;
    }
    if (rest[0].Equals("dump", StringComparison.OrdinalIgnoreCase) && rest.Length > 1)
    {
        var shown = types.FirstOrDefault(t => t.Id.ToString() == rest[1]) ?? throw new InvalidOperationException($"no injury type {rest[1]}");
        var raw = session.Memory.Read(shown.Address, 0x140);
        Console.WriteLine($"{shown.Name} ({shown.Id}) at 0x{shown.Address:X}");
        if (session.InjuryTypeFields.Read(shown) is { } det) Console.WriteLine($"  unique {det.UniqueId} min {det.MinimumDays} extra {det.ExtraDays} inactive {det.InactiveRatio} occurrence {det.OccurrenceRatio} match% {det.MatchInjuryPercentage} gk% {det.GoalkeeperPercentage} dc% {det.DefenderCentralPercentage} seriousness {det.Seriousness} age {det.AgeRule} season {det.Season} recurring {det.Recurring} contagious {det.Contagious} treatment {det.Treatment}");
        for (int i = 0; i < raw.Length; i += 16) Console.WriteLine($"  +{i:X3} {Convert.ToHexString(raw.AsSpan(i, 16))}");
        return 0;
    }
    if (rest[0].Equals("set", StringComparison.OrdinalIgnoreCase) && rest.Length > 3)
    {
        var target = types.FirstOrDefault(t => t.Id.ToString() == rest[1]) ?? throw new InvalidOperationException($"no injury type {rest[1]}");
        var details = session.InjuryTypeFields.Read(target) ?? throw new InvalidOperationException("type unreadable");
        var field = Enum.Parse<Fm27Editor.Core.Players.InjuryTypeField>(rest[2], true);
        double value = double.Parse(rest[3], System.Globalization.CultureInfo.InvariantCulture);
        Console.WriteLine($"{target.Name}: {field} {details.Value(field)} -> {value}");
        if (write) { session.InjuryTypeFields.Set(details, field, value); Console.WriteLine($"now {session.InjuryTypeFields.Read(target)!.Value(field)}"); } else Console.WriteLine("dry run; pass --write");
        return 0;
    }
    if (rest.Length < 2) { Console.Error.WriteLine("usage: injurytype list | dump <type id> | set <type id> <field> <value> | <player id> <type id> [--write]"); return 2; }
    var player = session.Players.LoadAll().FirstOrDefault(p => p.Id.ToString() == rest[0]) ?? throw new InvalidOperationException($"no player {rest[0]}");
    var injuries = session.ReadInjuries(player);
    if (injuries.Count == 0) { Console.WriteLine($"{player.DisplayName} has no injury"); return 0; }
    var injury = injuries[0];
    var type = types.FirstOrDefault(t => t.Id.ToString() == rest[1]) ?? throw new InvalidOperationException($"no injury type {rest[1]}");
    Console.WriteLine($"{player.DisplayName}: '{injury.Name}' ({injury.TypeId}) -> '{type.Name}' ({type.Id}), {injury.MinimumDays}+{injury.ExtraDays} days");
    if (!write) { Console.WriteLine("dry run; pass --write"); return 0; }
    new Fm27Editor.Core.Players.InjuryWriter(session.Memory, session.Profile, session.Game.ModuleBase).SetType(player, injury, type);
    var after = session.ReadInjuries(player)[0];
    Console.WriteLine($"now '{after.Name}' ({after.TypeId}), {after.MinimumDays}+{after.ExtraDays} days");
    return 0;
}

// clubnames <text>   id, full name and short name of every club whose name contains the text (read only)
int ClubNamesCommand(string text)
{
    using var session = Attach(writable: false);
    foreach (var c in session.LoadClubs(session.Players.LoadAll()).Where(c => (c.Name ?? "").Contains(text, StringComparison.OrdinalIgnoreCase) || (c.ShortName ?? "").Contains(text, StringComparison.OrdinalIgnoreCase)))
        Console.WriteLine($"  {c.Id,6} name '{c.Name}' short '{c.ShortName}'");
    return 0;
}

// natboth   staff who hold both a club contract and a national appointment with a full contract (read only)
int NationBothCommand()
{
    using var session = Attach(writable: false);
    int shown = 0, withNational = 0;
    foreach (var nation in session.Nations.LoadAll())
    {
        IReadOnlyList<Fm27Editor.Core.Nations.NationStaffRow> rows;
        try { rows = session.NationStaff.ReadDetails(nation.Address); } catch { continue; }
        foreach (var row in rows.Where(r => r.HasFullContract))
        {
            withNational++;
            if (session.StaffContracts.Read(row.Staff) is { } club && shown++ < 12)
                Console.WriteLine($"  {row.Staff.Id,9} {row.Staff.DisplayName,-24} {nation.DisplayName,-14} {row.JobName,-18} national expires {row.Expires?.ToString("yyyy-MM-dd") ?? "no limit"} | club {row.Staff.Club?.DisplayName} expires {club.Expires:yyyy-MM-dd} wage {club.Wage}");
        }
    }
    Console.WriteLine($"{withNational} national full contracts, {shown} also have a club contract");
    return 0;
}

// nationsquad <nation> [teamIndex]: list a nation's national teams and their squads (read only)
int NationSquadCommand(string line)
{
    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    using var session = Attach(writable: false);
    var nation = session.Nations.LoadAll().FirstOrDefault(n => n.DisplayName.Equals(parts[0], StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"no nation named '{parts[0]}'");
    var teams = session.NationSquads.ReadNation(nation.Address);
    int only = parts.Length > 1 ? int.Parse(parts[1]) : -1;
    Console.WriteLine($"{nation.DisplayName}: {teams.Count} national teams");
    for (int i = 0; i < teams.Count; i++)
    {
        if (only >= 0 && i != only) continue;
        var (team, squad) = teams[i];
        Console.WriteLine($"  [{i}] team 0x{team.Address:X} reputation {team.Reputation}: {squad.Count} players");
        var fields = session.PlayerFields;
        var gameDate = session.ReadGameDate();
        foreach (var p in squad)
        {
            string age = gameDate is { } date ? p.AgeOn(date)?.ToString() ?? "?" : "?";
            Console.WriteLine($"      {p.Id,9} {p.DisplayName,-26} {p.Club?.DisplayName,-22} {p.Nationality?.DisplayName,-15} age {age,3} CA {p.CurrentAbility,4} PA {p.PotentialAbility,4} condition {fields.Read(p, PlayerField.Condition)?.ToString() ?? "?"} asking {fields.Read(p, PlayerField.AskingPrice)?.ToString() ?? "?"}");
        }
    }
    return 0;
}


// fam <player>            show tactical familiarity fields (FM Studio "Tactical Effectiveness" layout)
// fam <player> <0..100>   set the tactical familiarity value
// fam club <clubId> <0..100>  set the value for every player in the club's squads
int FamiliarityCommand(string[] args)
{
    if (args.Length < 1) throw new ArgumentException("usage: fam <player> [value] | fam club <clubId> <value>");
    bool clubMode = args[0].Equals("club", StringComparison.OrdinalIgnoreCase);
    bool write = clubMode ? args.Length >= 3 : args.Length >= 2;
    using var session = Attach(writable: write);
    var access = session.PlayerFamiliarity;
    if (clubMode)
    {
        int clubId = int.Parse(args[1]);
        int value = write ? int.Parse(args[2]) : -1;
        var all = session.Players.LoadAll();
        var club = session.LoadClubs(all).FirstOrDefault(x => x.Id == clubId) ?? throw new InvalidOperationException($"no club {clubId}");
        var players = club.Teams.SelectMany(team => session.ReadSquad(team.Address)).DistinctBy(p => p.Address).ToArray();
        int changed = 0;
        foreach (var player in players)
        {
            var fam = access.Read(player.Address);
            if (fam is null) continue;
            if (value >= 0) { access.SetTactical(fam, value); changed++; }
        }
        Console.WriteLine($"{club.DisplayName}: {players.Length} players, {changed} updated");
        return 0;
    }
    var target = session.Players.LoadAll().FirstOrDefault(p => p.DisplayName.Contains(args[0], StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"no player named '{args[0]}'");
    var snapshot = access.Read(target.Address) ?? throw new InvalidOperationException("player familiarity is not readable");
    if (args.Length >= 2) access.SetTactical(snapshot, int.Parse(args[1]));
    var after = access.Read(target.Address)!;
    Console.WriteLine($"{target.DisplayName}: tactical {after.Tactical}/100 roles [{string.Join(",", after.Roles)}] condition {after.Condition}/10000 morale {after.Morale}/20 extra {after.Extra}/20");
    return 0;
}

int ClubFieldsCommand(int clubId)
{
    using var session = Attach(writable: false);
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId)
        ?? session.ScanAllClubs().SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var values = session.ClubFields.ReadAll(club.Address, club.FirstTeam?.Address ?? 0);
    Console.WriteLine($"{club.Name}: {values.Count} mapped fields");
    foreach (var (f, v) in values.OrderBy(kv => kv.Key)) Console.WriteLine($"  {f,-24} {v}");
    return 0;
}

// club <id>   addresses of the club object, its finance object, clause holder and teams (read only)
int ClubInfoCommand(int clubId)
{
    using var session = Attach(writable: false);
    var all = session.Players.LoadAll();
    var club = session.LoadClubs(all).SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var k = session.Profile.Club!;
    Console.WriteLine($"{club.Name} ({club.ShortName}) id {club.Id}");
    Console.WriteLine($"  club    0x{club.Address:X}  vtable +0x{k.ClubVtableRva:X}");
    Console.WriteLine($"  finance 0x{club.Finance:X}  (club+0x{k.Finance?.Pointer:X})");
    if (k.TransferClauses is { } tc) Console.WriteLine($"  clauses 0x{session.Memory.ReadUInt64(club.Address + (ulong)tc.Pointer):X}  (club+0x{tc.Pointer:X})");
    foreach (var t in club.Teams)
        Console.WriteLine($"  team    0x{t.Address:X}  {(t.IsFirstTeam ? "first team" : "")} squad {t.SquadCount}/{t.SquadCapacity}");
    Console.WriteLine($"  module  0x{session.ModuleBase:X}");
    return 0;
}

// probe <hexAddr> <hexSize> <depth> <value>...
//   Read-only search for values inside [addr, addr+size) and, when depth > 0, inside the first 0x1000 bytes of every
//   object a qword in that range points to (depth levels). Each value is matched as int32, int16 and (when 0-255) byte.
//   Values may be decimal or 0x-hex. Prints path as +off[->+off...] so the field can be read back as a chain.
int Probe(ulong address, int size, int depth, string[] values)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var wants = values.Select(v => v.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToInt64(v[2..], 16) : long.Parse(v)).ToArray();
    var seen = new HashSet<ulong>();
    void Scan(ulong at, int length, string path, int level)
    {
        if (!seen.Add(at)) return;
        var b = new byte[length];
        if (!mem.TryRead(at, b))
        {
            // Fall back to the largest readable prefix in 0x100 steps.
            int ok = 0;
            for (int n = 0x100; n <= length; n += 0x100) { if (mem.TryRead(at, b.AsSpan(0, n))) ok = n; else break; }
            if (ok == 0) return;
            length = ok;
        }
        for (int o = 0; o + 1 < length; o++)
            foreach (var w in wants)
            {
                if (o % 4 == 0 && o + 4 <= length && BitConverter.ToInt32(b, o) == w) Console.WriteLine($"{path}+0x{o:X} int32 = {w}");
                else if (o % 2 == 0 && w is >= short.MinValue and <= short.MaxValue && BitConverter.ToInt16(b, o) == w) Console.WriteLine($"{path}+0x{o:X} int16 = {w}");
                // Single bytes match almost everywhere, so they are reported only when one value is asked for.
                else if (wants.Length == 1 && w is >= 0 and <= 255 && b[o] == w) Console.WriteLine($"{path}+0x{o:X} byte = {w}");
            }
        if (level <= 0) return;
        for (int o = 0; o + 8 <= length; o += 8)
        {
            ulong q = BitConverter.ToUInt64(b, o);
            if (q < 0x10000 || q > 0x7FFF_FFFF_FFFF || q >= session.ModuleBase && q < session.ModuleBase + 0x1000_0000) continue;
            Scan(q, 0x1000, $"{path}+0x{o:X}->", level - 1);
        }
    }
    Scan(address, size, "", depth);
    return 0;
}

// clauses fee   every sell-on that stores the signed-for fee (not -1), with player and both clubs
int SellOnsWithFee()
{
    using var session = Attach(writable: false);
    var all = session.Players.LoadAll();
    var byId = all.Where(p => p.Id is not null).GroupBy(p => p.Id!.Value).ToDictionary(g => g.Key, g => g.First());
    foreach (var club in session.LoadClubs(all))
        foreach (var c in session.ReadTransferClauses(club.Address).Where(c => c.Kind == Fm27Editor.Core.Clubs.TransferClauseKind.SellOn && c.SignedFor is not null))
            Console.WriteLine($"{(byId.TryGetValue(c.PlayerId, out var p) ? p.DisplayName : c.PlayerId.ToString()),-26} at {club.DisplayName,-24} {c.Percentage}% to {c.Beneficiary?.DisplayName,-24} signed for {c.SignedFor:N0}");
    return 0;
}

// contract <name>                      show wage, expiry, playing time
// contract <name> Wage|Expires|PlayingTime <value>
// contractrm <name> <term index>   remove one bonus/clause term (cleanup for UI tests)
int ContractRemove(string name, int index)
{
    using var session = Attach(writable: true);
    var matches = session.Players.LoadAll().Where(p => p.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
    if (matches.Count != 1) { Console.Error.WriteLine($"{matches.Count} players named '{name}'"); return 2; }
    var player = matches[0];
    var details = session.Contracts.Read(player) ?? throw new InvalidOperationException("no contract");
    Console.WriteLine($"removing term {index} (type {details.Terms[index].Type} value {details.Terms[index].Value}): {details.Terms.Count} terms");
    session.Contracts.RemoveTerm(player, details, index);
    var after = session.Contracts.Read(session.Players.Reload(player.Address)!)!;
    Console.WriteLine($"after: {after.Terms.Count} terms -> {string.Join(", ", after.Terms.Select(t => $"type {t.Type} value {t.Value}"))}");
    return 0;
}
int ContractCommand(string name, string? field, string? text)
{
    using var session = Attach(writable: field is not null);
    var matches = session.Players.LoadAll().Where(p => p.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
    if (matches.Count != 1) { Console.Error.WriteLine($"{matches.Count} players named '{name}'"); return 2; }
    var p = matches[0];
    var w = new ContractWriter(session.Memory, session.Profile, session.ModuleBase);
    if (p.Contract is not { } c) { Console.Error.WriteLine("no contract"); return 2; }
    var d = w.Read(p)!;
    Console.WriteLine($"{p.DisplayName}: {d.Type}, wage {d.Wage}, loyalty {d.LoyaltyBonus}, number {d.SquadNumber}");
    Console.WriteLine($"  started {d.Started:yyyy-MM-dd} signed {d.Signed:yyyy-MM-dd} expires {d.Expires:yyyy-MM-dd} joined club {d.JoinedClub:yyyy-MM-dd}");
    Console.WriteLine($"  playing time {d.PlayingTime}, future {d.FuturePlayingTime} from {d.FuturePlayingTimeYear}, happiness {d.Happiness}, playing time happiness {d.PlayingTimeHappiness}");
    foreach (var t in d.Terms) Console.WriteLine($"  term {t.Index}: type {t.Type} value {t.Value} parameter {t.Parameter}");
    if (field is null) return 0;
    switch (field.ToLowerInvariant())
    {
        case "wage": w.SetWage(p, int.Parse(text!)); break;
        case "expires": w.SetExpiry(p, DateOnly.ParseExact(text!, "yyyy-MM-dd")); break;
        case "playingtime": w.SetPlayingTime(p, Enum.Parse<PlayingTime>(text!, true)); break;
        case "number": w.SetSquadNumber(p, d, int.Parse(text!)); break;
        case "joinedclub": w.SetJoinedClub(p, d, DateOnly.ParseExact(text!, "yyyy-MM-dd")); break;
        default: Console.Error.WriteLine($"unknown contract field '{field}'"); return 2;
    }
    var after = session.Players.Reload(p.Address)!;
    var ad = w.Read(after)!;
    Console.WriteLine($"after: wage {after.Contract?.Wage}, expires {after.Contract?.Expires:yyyy-MM-dd}, playing time {w.ReadPlayingTime(after)}, number {ad.SquadNumber}, joined club {ad.JoinedClub:yyyy-MM-dd}");
    return 0;
}

int Set(string name, string field, string text)
{
    bool born = field.Equals("Born", StringComparison.OrdinalIgnoreCase);
    DateOnly date = default;
    int value = 0;
    if (born ? !DateOnly.TryParseExact(text, "yyyy-MM-dd", out date) : !int.TryParse(text, out value))
    {
        Console.Error.WriteLine($"bad value '{text}' for {field}");
        return 2;
    }
    using var session = Attach(writable: true);
    var matches = session.Players.LoadAll().Where(p => p.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
    if (matches.Count != 1)
    {
        Console.Error.WriteLine($"{matches.Count} players named '{name}'");
        return 2;
    }
    var p = matches[0];
    var w = session.Writer;
    string before;
    if (Enum.TryParse<PlayerAttribute>(field, true, out var a)) { before = p.Attribute(a).ToString(); w.SetAttribute(p, a, value); }
    else if (Enum.TryParse<PlayerPosition>(field, true, out var pos)) { before = p.Position(pos).ToString(); w.SetPosition(p, pos, value); }
    else if (Enum.TryParse<PersonalityAttribute>(field, true, out var per)) { before = p.PersonalityValue(per).ToString(); w.SetPersonality(p, per, value); }
    else if (field.Equals("CA", StringComparison.OrdinalIgnoreCase)) { before = p.CurrentAbility.ToString(); w.SetCurrentAbility(p, value); }
    else if (field.Equals("PA", StringComparison.OrdinalIgnoreCase)) { before = p.PotentialAbility.ToString(); w.SetPotentialAbility(p, value); }
    else if (field.Equals("Height", StringComparison.OrdinalIgnoreCase)) { before = p.Height.ToString(); w.SetHeight(p, value); }
    else if (born) { before = $"{p.DateOfBirth:yyyy-MM-dd}"; w.SetDateOfBirth(p, date); }
    else
    {
        Console.Error.WriteLine($"unknown field '{field}'");
        return 2;
    }
    Console.WriteLine($"{p.DisplayName} {field}: {before} -> {text}");
    return 0;
}

int FindOffsets(string goldenPath, string outPath, string? gameDate)
{
    var game = GameProcess.Locate();
    using var memory = new WindowsProcessMemory(game.ProcessId, writable: false);
    var sw = Stopwatch.StartNew();
    var date = gameDate is null ? (DateOnly?)null : DateOnly.Parse(gameDate, System.Globalization.CultureInfo.InvariantCulture);
    var profile = new OffsetFinder(memory, game, Console.WriteLine).Find(GoldenPlayers.Load(goldenPath), date);
    File.WriteAllText(outPath, profile.ToJson());
    Console.WriteLine($"wrote {outPath} in {sw.Elapsed.TotalSeconds:F0} s");
    return 0;
}

int Transfer(string[] args)
{
    string sub = args.FirstOrDefault() ?? "";
    try
    {
        return sub switch
        {
            "dump" => TransferDump(args[1], args.ElementAtOrDefault(2)),
            "peek" => TransferPeek(args[1]),
            "offers" => TransferOffers(),
            "diff" => TransferShowDiff(args[1], args[2]),
            _ => Usage(),
        };
    }
    catch (IndexOutOfRangeException) { return Usage(); }
}

PlayerRecord FindOne(GameSession session, string name)
{
    var matches = session.Players.LoadAll().Where(p => p.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
    if (matches.Count != 1) throw new ArgumentOutOfRangeException(nameof(name), name, $"{matches.Count} players named '{name}'");
    return matches[0];
}

int TransferPeek(string name)
{
    using var session = Attach(writable: false);
    var player = FindOne(session, name);
    Console.WriteLine($"{player.DisplayName}  player 0x{player.Address:X}  game date {session.ReadGameDate()?.ToString("yyyy-MM-dd") ?? "unknown"}");
    Console.WriteLine($"club {player.Club?.DisplayName ?? "-"}  wage {player.Contract?.Wage.ToString() ?? "-"}  expires {player.Contract?.Expires?.ToString("yyyy-MM-dd") ?? "-"}  joined {player.Contract?.Joined?.ToString("yyyy-MM-dd") ?? "-"}");
    Console.WriteLine($"contract +0x330 -> 0x{player.Contract?.Address ?? 0:X}   pending +0x328 -> 0x{player.PendingContractAddress:X}");
    foreach (var clause in session.ReadClauses(player))
        Console.WriteLine($"clause type {clause.Type,3}  value {clause.Value}");
    if (player.Club is { } club) PrintFinance(session.ReadFinance(club.Address));
    return 0;
}

void PrintFinance(ClubFinance? finance)
{
    if (finance is null) { Console.WriteLine("finance unknown"); return; }
    Console.WriteLine($"finance 0x{finance.Address:X}  balance {finance.Balance}  budget {finance.TransferBudget}  spent {finance.SpentThisSeason}  wages {finance.WageSpend}");
}

int TransferOffers()
{
    using var session = Attach(writable: false);
    var players = session.Players.LoadAll();
    var withOffer = players.Where(p => p.PendingContractAddress != 0).ToList();
    Console.WriteLine($"{withOffer.Count} players have a pending contract pointer (player+0x328)");
    foreach (var player in withOffer.Take(40))
        Console.WriteLine($"{player.DisplayName,-28} club {player.Club?.DisplayName ?? "-",-24} contract 0x{player.Contract?.Address ?? 0:X} pending 0x{player.PendingContractAddress:X} clauses {session.ReadClauses(player).Count}");
    return 0;
}

int TransferDump(string name, string? outPath)
{
    using var session = Attach(writable: false);
    var player = FindOne(session, name);
    var snapshot = TransferSnapshot.Capture(session, player);
    string path = outPath ?? $"transfer-{string.Concat(name.Where(c => char.IsLetterOrDigit(c) || c == '-'))}-{DateTime.Now:yyyyMMdd-HHmmss}.json";
    snapshot.Save(path);
    Console.WriteLine($"{player.DisplayName}: saved {path}  ({snapshot.Objects.Count} objects, {snapshot.Clauses.Count} clauses)");
    Console.WriteLine($"club {snapshot.Club ?? "-"}  wage {snapshot.Wage?.ToString() ?? "-"}  expires {snapshot.Expires ?? "-"}  joined {snapshot.Joined ?? "-"}");
    return 0;
}

int TransferShowDiff(string beforePath, string afterPath)
{
    var before = TransferSnapshot.Load(beforePath);
    var after = TransferSnapshot.Load(afterPath);
    Console.WriteLine($"{before.PlayerName}: {before.CapturedAt} -> {after.CapturedAt}");
    var changes = TransferDiff.Compare(before, after);
    foreach (var c in changes)
    {
        string where = c.Name is null ? $"{c.Section,-8} +0x{c.Offset:X3}" : $"{c.Section,-8} {c.Name}";
        string values = c.PointerBefore is { } pb
            ? $"ptr 0x{pb:X} -> 0x{c.PointerAfter ?? 0:X}"
            : $"{c.IntBefore?.ToString() ?? c.Before} -> {c.IntAfter?.ToString() ?? c.After}";
        Console.WriteLine($"{where}  {values}");
    }
    if (changes.Count == 0) Console.WriteLine("no differences");
    return 0;
}

int StaffCareers()
{
    using var session = Attach(writable: false);
    var all = session.Staff.LoadAll();
    var reader = new CareerPlanReader(session.Memory, session.Profile, session.Game.ModuleBase);
    int containers = 0;
    var with = new List<(Fm27Editor.Core.Staff.StaffRecord S, IReadOnlyList<CareerPlan> P)>();
    foreach (var s in all)
        if (reader.Read(session.Owner(s)) is { } plans) { containers++; if (plans.Count > 0) with.Add((s, plans)); }
    Console.WriteLine($"{all.Count} staff, {containers} with a container, {with.Count} with plans");
    foreach (var (s, p) in with.OrderByDescending(x => x.S.CurrentReputation).Take(15))
        Console.WriteLine($"  {s.DisplayName} ({s.Club?.DisplayName}) job {s.JobCode}: " + string.Join("; ", p.Select(x => $"{x.ItemName} c{x.Condition} s{x.Scope} j{x.Job} t{x.ItemType}")));
    return 0;
}

int StaffKnowledgeCommand(string name)
{
    using var session = Attach(writable: false);
    var nations = session.Nations.LoadAll();
    foreach (var s in session.Staff.LoadAll().Where(s => s.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)))
        Console.WriteLine($"{s.DisplayName} ({s.Club?.DisplayName}): " + string.Join(", ", session.StaffKnowledge.Read(s, nations).Select(k => $"{k.Nation} {k.Value}")));
    return 0;
}

// tendencysurvey   RE: every tendency type code over all staff, with how many staff have it (read only)
int TendencySurvey()
{
    using var session = Attach(writable: false);
    var counts = new SortedDictionary<int, int>();
    int bad = 0;
    foreach (var s in session.Staff.LoadAll())
    {
        try { foreach (var t in session.StaffTendencies.Read(s)) counts[t.Type] = counts.GetValueOrDefault(t.Type) + 1; }
        catch (Exception) { bad++; }
    }
    Console.WriteLine($"{counts.Count} codes, {bad} unreadable");
    Console.WriteLine(string.Join("  ", counts.Select(kv => $"{kv.Key}:{kv.Value}")));
    return 0;
}

// natrivals <nation>: the Rival Nation rows the editor shows on a nation's Relationships page (read only)
int NatRivalsCommand(string query)
{
    using var session = Attach(writable: false);
    var all = session.Nations.LoadAll();
    var nation = all.FirstOrDefault(n => n.DisplayName.Equals(query.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"no nation named '{query}'");
    var rivals = session.NationRivalries.Read(nation, all);
    Console.WriteLine($"{nation.DisplayName} id {nation.Id}: {rivals.Count} rival nation row(s)");
    foreach (var r in rivals) Console.WriteLine($"  {r.Opponent,-24} value {r.Value}");
    return 0;
}

// termstats: for every contract bonus/clause type in the game, how often its value and its parameter hold -1 (read only)
int TermStatsCommand()
{
    using var session = Attach(writable: false);
    var stats = new SortedDictionary<short, (int Count, int ValueMinus, int ParamMinus, int ValueSet, int ParamSet, int MaxParam, long MaxValue)>();
    foreach (var player in session.Players.LoadAll())
    {
        Fm27Editor.Core.Players.ContractDetails? d;
        try { d = session.Contracts.Read(player); } catch { continue; }
        if (d is null) continue;
        foreach (var t in d.Terms)
        {
            stats.TryGetValue(t.Type, out var s);
            s.Count++;
            if (t.Value == -1) s.ValueMinus++; else s.ValueSet++;
            if (t.Parameter == -1) s.ParamMinus++; else s.ParamSet++;
            s.MaxParam = Math.Max(s.MaxParam, t.Parameter);
            s.MaxValue = Math.Max(s.MaxValue, t.Value);
            stats[t.Type] = s;
        }
    }
    Console.WriteLine("type  count  value=-1 value set  param=-1 param set  maxParam maxValue");
    foreach (var (type, s) in stats)
        Console.WriteLine($"{type,4}  {s.Count,6}  {s.ValueMinus,8} {s.ValueSet,9}  {s.ParamMinus,8} {s.ParamSet,9}  {s.MaxParam,8} {s.MaxValue}");
    return 0;
}

// minusstats: for every club and nation field the editor reads, how many records hold -1 (read only)
int MinusStatsCommand()
{
    using var session = Attach(writable: false);
    void Survey<T>(string label, IEnumerable<T> records)
    {
        var stats = new SortedDictionary<string, (int Total, int Minus, int Zero, int ByteMax, int UShortMax, int IntMax)>();
        var numeric = typeof(T).GetProperties().Where(p => p.GetIndexParameters().Length == 0 &&
            (Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType) is var t &&
            (t == typeof(byte) || t == typeof(sbyte) || t == typeof(short) || t == typeof(ushort) || t == typeof(int) || t == typeof(uint) || t == typeof(long))).ToArray();
        int count = 0;
        foreach (var record in records)
        {
            count++;
            foreach (var property in numeric)
            {
                object? raw = property.GetValue(record);
                if (raw is null) continue;
                long value = Convert.ToInt64(raw);
                stats.TryGetValue(property.Name, out var s);
                s.Total++; if (value == -1) s.Minus++; if (value == 0) s.Zero++;
                if (value == 255) s.ByteMax++; if (value == 65535) s.UShortMax++; if (value == int.MaxValue) s.IntMax++;
                stats[property.Name] = s;
            }
        }
        Console.WriteLine($"{label}: {count} records (raw counts; meanings not inferred)");
        foreach (var (name, s) in stats)
            Console.WriteLine($"  {name,-30} n={s.Total} -1={s.Minus} 0={s.Zero} 255={s.ByteMax} 65535={s.UShortMax} intmax={s.IntMax}");
    }
    var players = session.Players.LoadAll();
    var clubs = session.LoadClubs(players);
    var club = new SortedDictionary<string, (int Total, int Minus)>();
    foreach (var c in clubs)
    {
        ulong team = c.Teams.Count > 0 ? c.Teams[0].Address : 0;
        foreach (var (field, value) in session.ClubFields.ReadAll(c.Address, team))
        {
            club.TryGetValue(field.ToString(), out var s);
            s.Total++; if (value == -1) s.Minus++;
            club[field.ToString()] = s;
        }
    }
    Console.WriteLine($"clubs: {clubs.Count}");
    foreach (var (name, s) in club.Where(kv => kv.Value.Minus > 0)) Console.WriteLine($"  {name,-34} -1 in {s.Minus} of {s.Total}");
    var nation = new SortedDictionary<string, (int Total, int Minus)>();
    var nations = session.Nations.LoadAll();
    foreach (var n in nations)
        foreach (var (field, value) in session.NationFields.ReadAll(n.Address))
        {
            nation.TryGetValue(field.ToString(), out var s);
            s.Total++; if (value == -1) s.Minus++;
            nation[field.ToString()] = s;
        }
    var contracts = new SortedDictionary<string, (int Total, int Minus)>();
    void Count(string name, long? value) { if (value is null) return; contracts.TryGetValue(name, out var s); s.Total++; if (value == -1) s.Minus++; contracts[name] = s; }
    foreach (var player in players)
    {
        Fm27Editor.Core.Players.ContractDetails? d;
        try { d = session.Contracts.Read(player); } catch { continue; }
        if (d is null) continue;
        Count("Wage", d.Wage); Count("LoyaltyBonus", d.LoyaltyBonus); Count("SquadNumber", d.SquadNumber); Count("Happiness", d.Happiness);
        Count("PlayingTimeHappiness", d.PlayingTimeHappiness); Count("FuturePlayingTimeYear", d.FuturePlayingTimeYear);
    }
    Console.WriteLine("contracts:");
    foreach (var (name, s) in contracts.Where(kv => kv.Value.Minus > 0)) Console.WriteLine($"  {name,-34} -1 in {s.Minus} of {s.Total}");
    Console.WriteLine($"nations: {nations.Count}");
    foreach (var (name, s) in nation.Where(kv => kv.Value.Minus > 0)) Console.WriteLine($"  {name,-34} -1 in {s.Minus} of {s.Total}");
    Survey("staff", session.Staff.LoadAll());
    Survey("cities", session.Cities.LoadAll());
    Survey("competitions", session.Competitions.LoadAll());
    Survey("awards", session.Awards.LoadAll());
    Survey("referees", session.ReadReferees());
    var stadiums = session.Stadiums.LoadAll();
    Survey("stadiums", stadiums);
    var stadiumFields = new SortedDictionary<string, (int Total, int Minus, int Zero, int ByteMax, int UShortMax, int IntMax)>();
    foreach (var stadium in stadiums)
        foreach (var (field, value) in session.StadiumWriter.ReadFields(stadium.Address))
        {
            string name = field.ToString(); stadiumFields.TryGetValue(name, out var s); s.Total++;
            if (value == -1) s.Minus++; if (value == 0) s.Zero++; if (value == 255) s.ByteMax++;
            if (value == 65535) s.UShortMax++; if (value == int.MaxValue) s.IntMax++;
            stadiumFields[name] = s;
        }
    Console.WriteLine("stadium fields:");
    foreach (var (name, s) in stadiumFields) Console.WriteLine($"  {name,-30} n={s.Total} -1={s.Minus} 0={s.Zero} 255={s.ByteMax} 65535={s.UShortMax} intmax={s.IntMax}");
    var injuries = new List<PlayerInjury>(); var bans = new List<PlayerBan>(); var careers = new List<CareerPlan>();
    var relationships = new List<PlayerRelationship>(); var languages = new List<PlayerLanguage>();
    var failures = new Dictionary<string, int>();
    void ReadPlayerTab(string name, Action read)
    {
        try { read(); }
        catch (Exception ex) when (ex is MemoryAccessException or InvalidOperationException)
        {
            failures[name] = failures.GetValueOrDefault(name) + 1;
        }
    }
    foreach (var player in players)
    {
        ReadPlayerTab("injuries", () => injuries.AddRange(session.ReadInjuries(player)));
        ReadPlayerTab("unavailability", () => bans.AddRange(session.ReadBans(player)));
        ReadPlayerTab("career plans", () => { if (session.CareerPlans.Read(player) is { } plans) careers.AddRange(plans); });
        ReadPlayerTab("relationships", () => relationships.AddRange(session.ReadStatuses(player).Relationships));
        ReadPlayerTab("languages", () => languages.AddRange(session.ReadLanguages(player)));
    }
    Survey("player injuries", injuries); Survey("player unavailability", bans); Survey("player career plans", careers);
    Survey("player relationships", relationships); Survey("player languages", languages);
    Console.WriteLine($"preferred moves: {players.Count(p => p.PreferredMoveBits is null)} unavailable, {players.Count(p => p.PreferredMoveBits == 0)} empty of {players.Count}");
    foreach (var (name, count) in failures) Console.WriteLine($"  {name}: {count} read failures");
    return 0;
}

// savewatch [seconds]   prints once a second what could tell one loaded save from another (read only)
int SaveWatchCommand(int seconds)
{
    using var session = Attach(writable: false);
    var p = session.Profile;
    ulong module = session.ModuleBase;
    ulong Rd(ulong at) { try { return session.Memory.ReadUInt64(at); } catch { return 0; } }
    for (int i = 0; i < seconds; i++)
    {
        ulong root = module + p.PlayerListRootRva;
        ulong begin = Rd(root + (ulong)p.PlayerListBeginOffset), end = Rd(root + (ulong)p.PlayerListEndOffset);
        ulong first = begin == 0 ? 0 : Rd(begin);
        var parts = new List<string>
        {
            $"playersBegin=0x{begin:X}", $"playersEnd=0x{end:X}", $"count={(end > begin ? (end - begin) / 8 : 0)}", $"first=0x{first:X}",
            $"date={session.ReadGameDate():yyyy-MM-dd}",
        };
        if (p.Nation?.ListRootRva is { } n) parts.Add($"nations=0x{Rd(module + n):X}");
        if (p.Competition?.ListRootRva is { } c) parts.Add($"competitions=0x{Rd(module + (ulong)c):X}");
        if (p.Award?.ListRootRva is { } a) parts.Add($"awards=0x{Rd(module + (ulong)a):X}");
        if (p.Proposals?.ListRootRva is { } o) parts.Add($"offerRoot=0x{Rd(module + (ulong)o):X}");
        if (p.Promises?.GlobalRootRva is { } g) parts.Add($"promiseRoot=0x{Rd(module + (ulong)g):X}");
        Console.WriteLine($"{DateTime.Now:HH:mm:ss} " + string.Join(" ", parts));
        Thread.Sleep(1000);
    }
    return 0;
}

// vtsurvey   tallies the vtables of the club vision holders and rows, and of staff, against the profile (read only)
int VtSurveyCommand()
{
    using var session = Attach(writable: false);
    var p = session.Profile;
    ulong module = session.ModuleBase;
    ulong Q(ulong at) { try { return session.Memory.ReadUInt64(at); } catch { return 0; } }
    string Name(ulong vtableAbs)
    {
        ulong col = Q(vtableAbs - 8);
        if (col < module || col > module + 0x20000000) return "?";
        try
        {
            if (BitConverter.ToUInt32(session.Memory.Read(col, 4)) != 1) return "?";
            uint typeRva = BitConverter.ToUInt32(session.Memory.Read(col + 12, 4));
            var raw = session.Memory.Read(module + typeRva + 16, 120);
            int end = Array.IndexOf(raw, (byte)0);
            return System.Text.Encoding.ASCII.GetString(raw, 0, end < 0 ? raw.Length : end);
        }
        catch { return "?"; }
    }
    void Print(string title, Dictionary<ulong, int> tally, IEnumerable<uint> known)
    {
        Console.WriteLine(title);
        foreach (var (vt, n) in tally.OrderByDescending(x => x.Value))
        {
            uint rva = vt >= module ? (uint)(vt - module) : 0;
            Console.WriteLine($"  rva 0x{rva:X} x{n} {(known.Contains(rva) ? "[in profile]" : "[NOT in profile]")} {Name(vt)}");
        }
    }
    var all = session.Players.LoadAll();
    var clubs = session.LoadClubs(all);
    if (p.Club?.Lists is { } l)
    {
        var holders = new Dictionary<ulong, int>(); var rows = new Dictionary<ulong, int>();
        foreach (var club in clubs)
        {
            ulong team = club.FirstTeam?.Address ?? 0; if (team == 0) continue;
            ulong detail = Q(team + (ulong)l.TeamDetails); if (detail == 0) continue;
            ulong holder = Q(detail + (ulong)l.Vision); if (holder == 0) continue;
            ulong hv = Q(holder); holders[hv] = holders.GetValueOrDefault(hv) + 1;
            ulong header = Q(holder + (ulong)l.VisionVector); if (header == 0) continue;
            ulong begin = Q(header), end = Q(header + 8);
            if (begin == 0 || end <= begin || (end - begin) / 8 > 5000) continue;
            for (ulong at = begin; at < end; at += 8) { ulong row = Q(at); if (row == 0) continue; ulong rv = Q(row); rows[rv] = rows.GetValueOrDefault(rv) + 1; }
        }
        var knownHolder = new List<uint> { l.VisionHolderVtable }; knownHolder.AddRange(l.VisionHolderVtables.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => Convert.ToUInt32(x, 16)));
        var knownCulture = new List<uint> { l.CultureVtable }; knownCulture.AddRange(l.CultureVtables.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => Convert.ToUInt32(x, 16)));
        Print($"vision holders ({clubs.Count} clubs)", holders, knownHolder);
        Print("vision rows (culture objects)", rows, knownCulture);
        int withVision = 0, total = 0;
        foreach (var club in clubs) { if (club.FirstTeam is not { } t) continue; total++; try { if (session.ReadClubVision(t.Address, null).Count > 0) withVision++; } catch { } }
        Console.WriteLine($"clubs whose vision the profile can read: {withVision} of {total}");
    }
    if (p.Staff is { } so)
    {
        ulong root = so.DirectStaffVector ? module + so.PersonListRootRva : Q(module + so.PersonListRootRva);
        ulong begin = Q(root + (ulong)so.PersonListOffset), end = Q(root + (ulong)so.PersonListOffset + 8);
        var persons = new Dictionary<ulong, int>(); var staffParts = new Dictionary<(ulong, ulong), int>();
        if (begin != 0 && end > begin && (end - begin) / 8 < 200000)
            for (ulong at = begin; at < end; at += 8)
            {
                ulong person = Q(at); if (person == 0) continue;
                ulong vt = Q(person); persons[vt] = persons.GetValueOrDefault(vt) + 1;
                ulong sv = Q(person - 0x100); staffParts[(vt, sv)] = staffParts.GetValueOrDefault((vt, sv)) + 1;
            }
        Print("staff entries by first vtable", persons, so.Kinds.Select(k => k.PersonVtableRva).Concat(so.Kinds.Select(k => k.StaffVtableRva)));
        foreach (var (key, n) in staffParts.OrderByDescending(x => x.Value).Take(8))
            Console.WriteLine($"  entry vtable 0x{(key.Item1 >= module ? key.Item1 - module : 0):X} / vtable 0x100 below 0x{(key.Item2 >= module ? key.Item2 - module : 0):X} x{n} {Name(key.Item2)}");
    }
    return 0;
}

// findclub <text>   clubs whose name contains the text, with their id and how many vision rows the profile reads (read only)
int FindClubCommand(string text)
{
    using var session = Attach(writable: false);
    foreach (var club in session.LoadClubs(session.Players.LoadAll()).Where(c => (c.Name ?? "").Contains(text, StringComparison.OrdinalIgnoreCase)).Take(20))
    {
        int rows = club.FirstTeam is { } t ? session.ReadClubVision(t.Address, null).Count : -1;
        Console.WriteLine($"id {club.Id} {club.Name}: vision rows {rows}");
    }
    return 0;
}

// compidx <n...>   the competitions whose Index or Id is one of the numbers (read only)
int CompIdxCommand(int[] numbers)
{
    using var session = Attach(writable: false);
    var all = session.Competitions.LoadAll();
    Console.WriteLine($"{all.Count} competitions");
    foreach (int n in numbers)
        foreach (var c in all.Where(c => c.Index == n || c.Id == n))
            Console.WriteLine($"{n}: index {c.Index} id {c.Id} '{c.DisplayName}' (matches {(c.Index == n ? "Index" : "")}{(c.Id == n ? " Id" : "")})");
    return 0;
}

// promisescan   players that hold promise records at player+0x360 (the Game Pass layout), and the global vector holding them (read only)
int PromiseScanCommand()
{
    using var session = Attach(writable: false);
    ulong Q(ulong at) { try { return session.Memory.ReadUInt64(at); } catch { return 0; } }
    int found = 0;
    foreach (var player in session.Players.LoadAll())
    {
        ulong holder = Q(player.Address + 0x360); if (holder == 0) continue;
        ulong begin = Q(holder), end = Q(holder + 8);
        if (begin == 0 || end <= begin || (end - begin) / 8 > 64) continue;
        ulong record = Q(begin); if (record == 0) continue;
        ulong person = Q(record + 0x20);
        bool ownerOk = person == player.Address + 0x288;
        ushort type = BitConverter.ToUInt16(session.Memory.Read(record + 0x4E, 2));
        Console.WriteLine($"{player.DisplayName} (ID {player.Id}): {(end - begin) / 8} record(s), first 0x{record:X} type {type} owner {(ownerOk ? "ok" : "mismatch")} holder 0x{holder:X} begin 0x{begin:X}");
        if (++found >= 12) break;
    }
    Console.WriteLine($"{found} player(s) shown");
    return 0;
}

// vectorroot <record hex> <vector offset hex>   module data pointers P whose vector at P+offset holds the record (read only)
int VectorRootCommand(ulong record, int vectorOffset)
{
    using var session = Attach(writable: false);
    var mem = session.Memory; ulong b = session.Game.ModuleBase;
    ulong Q(ulong at) { try { return mem.ReadUInt64(at); } catch { return 0; } }
    foreach (var region in session.Process.EnumerateWritableRegions(b, session.Game.ModuleEnd).Where(r => r.IsModuleData))
        for (ulong off = 0; off < region.Size; off += 0x10000)
        {
            int n = (int)Math.Min(0x10000UL, region.Size - off);
            var buf = new byte[n];
            if (!mem.TryRead(region.Start + off, buf)) continue;
            for (int i = 0; i + 8 <= n; i += 8)
            {
                ulong obj = BitConverter.ToUInt64(buf, i);
                if (obj < 0x10000 || obj >= 0x800000000000UL || (obj >= b && obj < session.Game.ModuleEnd)) continue;
                ulong begin = Q(obj + (ulong)vectorOffset), end = Q(obj + (ulong)vectorOffset + 8);
                if (begin == 0 || end <= begin || (end - begin) % 8 != 0 || (end - begin) / 8 > 5_000_000 || (end - begin) / 8 < 20) continue;
                byte[] items; try { items = mem.Read(begin, (int)(end - begin)); } catch { continue; }
                for (int k = 0; k + 8 <= items.Length; k += 8)
                    if (BitConverter.ToUInt64(items, k) == record)
                    {
                        Console.WriteLine($"module+0x{region.Start + off + (ulong)i - b:X} -> object 0x{obj:X}; vector at +0x{vectorOffset:X}: {(end - begin) / 8} item(s), record at index {k / 8}");
                        break;
                    }
            }
        }
    return 0;
}

// personsof <vtable rva hex> [n]   the first entries of the person vector whose first qword is that vtable, with their addresses (read only)
int PersonsOfCommand(uint vtableRva, int count)
{
    using var session = Attach(writable: false);
    var so = session.Profile.Staff!; ulong module = session.ModuleBase;
    ulong Q(ulong at) { try { return session.Memory.ReadUInt64(at); } catch { return 0; } }
    ulong root = so.DirectStaffVector ? module + so.PersonListRootRva : Q(module + so.PersonListRootRva);
    ulong begin = Q(root + (ulong)so.PersonListOffset), end = Q(root + (ulong)so.PersonListOffset + 8);
    int shown = 0, total = 0;
    for (ulong at = begin; at < end; at += 8)
    {
        ulong person = Q(at); if (person == 0 || Q(person) != module + vtableRva) continue;
        total++;
        if (shown++ < count) Console.WriteLine($"0x{person:X}");
    }
    Console.WriteLine($"{total} entries with vtable +0x{vtableRva:X}");
    return 0;
}

// agentchunk <record hex>   walks the 0xE0-byte records of the same vtable around one record: chunk start, count (read only)
int AgentChunkCommand(ulong record)
{
    using var session = Attach(writable: false);
    ulong Q(ulong at) { try { return session.Memory.ReadUInt64(at); } catch { return 0; } }
    ulong vt = Q(record), start = record, end = record;
    while (Q(start - 0xE0) == vt) start -= 0xE0;
    while (Q(end + 0xE0) == vt) end += 0xE0;
    Console.WriteLine($"vtable 0x{vt:X} (+0x{vt - session.ModuleBase:X}); chunk start 0x{start:X}, last record 0x{end:X}, {(end - start) / 0xE0 + 1} records");
    Console.WriteLine("heapfind pattern: " + Convert.ToHexString(BitConverter.GetBytes(start)));
    return 0;
}

// chunkslot <chunk hex>   module data slots S where [[S]] equals the chunk address (read only)
int ChunkSlotCommand(ulong chunk)
{
    using var session = Attach(writable: false);
    var mem = session.Memory; ulong b = session.Game.ModuleBase;
    ulong Q(ulong at) { try { return mem.ReadUInt64(at); } catch { return 0; } }
    foreach (var region in session.Process.EnumerateWritableRegions(b, session.Game.ModuleEnd).Where(r => r.IsModuleData))
        for (ulong off = 0; off < region.Size; off += 0x10000)
        {
            int n = (int)Math.Min(0x10000UL, region.Size - off);
            var buf = new byte[n];
            if (!mem.TryRead(region.Start + off, buf)) continue;
            for (int i = 0; i + 8 <= n; i += 8)
            {
                ulong holder = BitConverter.ToUInt64(buf, i);
                if (holder < 0x10000 || holder >= 0x800000000000UL || (holder >= b && holder < session.Game.ModuleEnd)) continue;
                for (int k = 0; k <= 0x300; k += 8) if (Q(holder + (ulong)k) == chunk) Console.WriteLine($"module+0x{region.Start + off + (ulong)i - b:X} -> object 0x{holder:X}, chunk pointer at +0x{k:X}");
            }
        }
    return 0;
}

// slotsbyname <text>   module data slots whose object's class (RTTI name) contains the text: slot rva, vtable rva, class (read only)
int SlotsByNameCommand(string text)
{
    using var session = Attach(writable: false);
    var mem = session.Memory; ulong b = session.Game.ModuleBase;
    ulong Q(ulong at) { try { return mem.ReadUInt64(at); } catch { return 0; } }
    string Name(ulong vtableAbs)
    {
        ulong col = Q(vtableAbs - 8);
        if (col < b || col > b + 0x20000000) return "";
        try
        {
            if (BitConverter.ToUInt32(mem.Read(col, 4)) != 1) return "";
            uint typeRva = BitConverter.ToUInt32(mem.Read(col + 12, 4));
            var raw = mem.Read(b + typeRva + 16, 120);
            int end = Array.IndexOf(raw, (byte)0);
            return System.Text.Encoding.ASCII.GetString(raw, 0, end < 0 ? raw.Length : end);
        }
        catch { return ""; }
    }
    var seen = new HashSet<ulong>();
    foreach (var region in session.Process.EnumerateWritableRegions(b, session.Game.ModuleEnd).Where(r => r.IsModuleData))
        for (ulong off = 0; off < region.Size; off += 0x10000)
        {
            int n = (int)Math.Min(0x10000UL, region.Size - off);
            var buf = new byte[n];
            if (!mem.TryRead(region.Start + off, buf)) continue;
            for (int i = 0; i + 8 <= n; i += 8)
            {
                ulong obj = BitConverter.ToUInt64(buf, i);
                if (obj < 0x10000 || obj >= 0x800000000000UL || (obj >= b && obj < session.Game.ModuleEnd) || !seen.Add(obj)) continue;
                ulong vt = Q(obj);
                if (vt < b || vt >= session.Game.ModuleEnd) continue;
                string name = Name(vt);
                if (name.Contains(text, StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine($"slot module+0x{region.Start + off + (ulong)i - b:X} -> object 0x{obj:X}, vtable +0x{vt - b:X} {name}");
            }
        }
    return 0;
}

// agrsurvey   the classes of the targets in every nation's agreement rows, by slot (read only)
int AgrSurveyCommand()
{
    using var session = Attach(writable: false);
    var mem = session.Memory; ulong b = session.ModuleBase;
    ulong Q(ulong at) { try { return mem.ReadUInt64(at); } catch { return 0; } }
    string Name(ulong vtableAbs)
    {
        ulong col = Q(vtableAbs - 8);
        if (col < b || col > b + 0x20000000) return "?";
        try
        {
            if (BitConverter.ToUInt32(mem.Read(col, 4)) != 1) return "?";
            uint typeRva = BitConverter.ToUInt32(mem.Read(col + 12, 4));
            var raw = mem.Read(b + typeRva + 16, 120); int end = Array.IndexOf(raw, (byte)0);
            return System.Text.Encoding.ASCII.GetString(raw, 0, end < 0 ? raw.Length : end);
        }
        catch { return "?"; }
    }
    var tally = new Dictionary<(int Vector, int Slot, ulong Vt), int>();
    foreach (var nation in session.Nations.LoadAll())
        foreach (int vector in new[] { 0xA8, 0x90 })
        {
            ulong begin = Q(nation.Address + (ulong)vector), end = Q(nation.Address + (ulong)vector + 8);
            if (begin == 0 || end <= begin || (end - begin) / 8 > 64) continue;
            for (ulong at = begin; at < end; at += 8)
            {
                ulong row = Q(at); if (row == 0) continue;
                foreach (int slot in new[] { 0, 8, 0x10, 0x18, 0x20 })
                {
                    ulong target = Q(row + (ulong)slot); if (target == 0) continue;
                    ulong vt = Q(target); if (vt < b || vt > b + 0x20000000) continue;
                    var key = (vector, slot, vt); tally[key] = tally.GetValueOrDefault(key) + 1;
                }
            }
        }
    foreach (var (key, n) in tally.OrderBy(x => x.Key.Vector).ThenBy(x => x.Key.Slot).ThenByDescending(x => x.Value))
        Console.WriteLine($"vector +0x{key.Vector:X} slot +0x{key.Slot:X}: vtable +0x{key.Vt - b:X} x{n} {Name(key.Vt)}");
    return 0;
}

// stadiumowner <text>   stadiums whose owning club's name contains the text (read only)
int StadiumOwnerCommand(string text)
{
    using var session = Attach(writable: false);
    var all = session.Stadiums.LoadAll();
    int owned = all.Count(x => x.Owner is not null);
    Console.WriteLine($"{all.Count} stadiums, {owned} with an owner");
    foreach (var x in all.Where(x => (x.Owner?.DisplayName ?? "").Contains(text, StringComparison.OrdinalIgnoreCase)).Take(10))
        Console.WriteLine($"id {x.Id} '{x.Name}' owner {x.Owner?.DisplayName} 0x{x.Address:X}");
    return 0;
}

// futureaddr [player id]   the offer and date addresses of a player's future transfer; without an id, the first 5 players that have one (read only)
int FutureAddrCommand(int id)
{
    using var session = Attach(writable: false);
    int shown = 0;
    foreach (var p in session.Players.LoadAll().Where(p => id == 0 || p.Id == id))
    {
        var offers = session.Proposals.Read(p);
        foreach (var f in session.FutureTransfers.Read(p, offers))
        {
            Console.WriteLine($"{p.DisplayName} (ID {p.Id}): offer id {f.Offer.Id} at 0x{f.Offer.Address:X} ({Convert.ToHexString(BitConverter.GetBytes(f.Offer.Address))}), date record 0x{f.DateAddress:X}, {f.Offer.From} -> {f.Offer.To}");
            shown++;
        }
        if (id == 0 && shown >= 5) break;
    }
    return 0;
}

// future-remove <player id> [--write]   takes the player's future transfer back out; without --write it only checks the plan
int FutureRemoveCommand(int id, bool write)
{
    using var session = Attach(writable: write);
    var player = session.Players.LoadAll().Single(p => p.Id == id);
    var transfer = session.FutureTransfers.Read(player, session.Proposals.Read(player)).FirstOrDefault()
        ?? throw new InvalidOperationException($"{player.DisplayName} has no future transfer");
    var remover = new FutureTransferRemover(session.Memory, session.Profile, session.ModuleBase);
    var plan = remover.Prepare(player, transfer);
    Console.WriteLine($"{player.DisplayName}: offer {transfer.Offer.Id} {transfer.Offer.From} -> {transfer.Offer.To}, {transfer.Date:yyyy-MM-dd}; {plan.Writes.Count} writes");
    if (!write) { Console.WriteLine("Plan only. Add --write to apply."); return 0; }
    session.RemoveFutureTransfer(player, transfer);
    Console.WriteLine("Removed; the player no longer has a future transfer.");
    return 0;
}
// gamecheck [feature]   reads every feature on the attached build (Game Pass or Steam); alias: steamcheck
int RunGameCheck(string[] args) { SteamCheck.Only = args.Length > 1 ? args[1] : ""; return SteamCheck.Run(Attach(writable: false)); }

// nationnames   every nation the profile finds, in list order, with its id (read only)
int NationNamesCommand()
{
    using var session = Attach(writable: false);
    var all = session.Nations.LoadAll();
    var no = session.Profile.Nation!;
    ulong b = session.ModuleBase;
    ulong Q(ulong at) { try { return session.Memory.ReadUInt64(at); } catch { return 0; } }
    ulong root = Q(b + (ulong)no.ListRootRva!.Value), begin = Q(root + (ulong)no.ListOffset!.Value), end = Q(root + (ulong)no.ListOffset!.Value + 8);
    var vts = new Dictionary<ulong, int>();
    for (ulong at = begin; at < end; at += 8) { ulong v = Q(Q(at)); vts[v] = vts.GetValueOrDefault(v) + 1; }
    Console.WriteLine($"raw vector: {(end - begin) / 8} pointers; vtables: " + string.Join(", ", vts.Select(kv => $"+0x{(kv.Key >= b ? kv.Key - b : 0):X} x{kv.Value}")) + $"; profile nation vtable +0x{no.VtableRva:X}");
    Console.WriteLine($"{all.Count} nations");
    foreach (var n in all) Console.WriteLine($"{n.Id} {n.DisplayName}");
    return 0;
}

// vectorsof <vtable rva hex> <min count>   module data slots whose object holds (within 0x810 bytes) a vector of at least that many pointers to objects of the class (read only)
int VectorsOfCommand(uint vtableRva, int minCount)
{
    using var session = Attach(writable: false);
    var mem = session.Memory; ulong b = session.Game.ModuleBase;
    ulong Q(ulong at) { try { return mem.ReadUInt64(at); } catch { return 0; } }
    foreach (var region in session.Process.EnumerateWritableRegions(b, session.Game.ModuleEnd).Where(r => r.IsModuleData))
        for (ulong off = 0; off < region.Size; off += 0x10000)
        {
            int n = (int)Math.Min(0x10000UL, region.Size - off);
            var buf = new byte[n];
            if (!mem.TryRead(region.Start + off, buf)) continue;
            for (int i = 0; i + 8 <= n; i += 8)
            {
                ulong obj = BitConverter.ToUInt64(buf, i);
                if (obj < 0x10000 || obj >= 0x800000000000UL || (obj >= b && obj < session.Game.ModuleEnd)) continue;
                var holder = new byte[0x810];
                if (!mem.TryRead(obj, holder)) continue;
                for (int at = 0; at + 24 <= holder.Length; at += 8)
                {
                    ulong begin = BitConverter.ToUInt64(holder, at), end = BitConverter.ToUInt64(holder, at + 8);
                    if (begin < 0x10000 || end <= begin || (end - begin) % 8 != 0 || (end - begin) / 8 < (ulong)minCount || (end - begin) / 8 > 100000) continue;
                    if (Q(Q(begin)) != b + vtableRva || Q(Q(end - 8)) != b + vtableRva) continue;
                    Console.WriteLine($"slot module+0x{region.Start + off + (ulong)i - b:X} -> object 0x{obj:X}, vector at +0x{at:X}: {(end - begin) / 8} items");
                }
            }
        }
    return 0;
}

// tablewalk <slot rva hex> [record vtable rva hex]   walks a DATABASE_TABLE (a red-black tree) from its module slot; counts records, and how many have the vtable (read only)
int TableWalkCommand(uint slotRva, uint recordVtable)
{
    using var session = Attach(writable: false);
    ulong b = session.ModuleBase;
    ulong Q(ulong at) { try { return session.Memory.ReadUInt64(at); } catch { return 0; } }
    ulong table = Q(b + slotRva), head = Q(table + 0x20);
    Console.WriteLine($"table 0x{table:X} head 0x{head:X} left 0x{Q(head):X} parent 0x{Q(head + 8):X} right 0x{Q(head + 0x10):X} size? {Q(table + 0x28)}");
    var seen = new HashSet<ulong>(); var records = new List<ulong>();
    var stack = new Stack<ulong>(); ulong node = Q(head + 8);
    while ((stack.Count > 0 || (node != head && node != 0)) && seen.Count < 1_000_000)
    {
        while (node != head && node != 0 && seen.Add(node)) { stack.Push(node); node = Q(node); }
        if (stack.Count == 0) break;
        node = stack.Pop();
        records.Add(Q(node + 0x20));
        node = Q(node + 0x10);
    }
    int matching = recordVtable == 0 ? 0 : records.Count(r => Q(r) == b + recordVtable);
    Console.WriteLine($"{records.Count} nodes" + (recordVtable != 0 ? $", {matching} with vtable +0x{recordVtable:X}" : ""));
    foreach (var r in records.Take(3)) Console.WriteLine($"  record 0x{r:X} first qword 0x{Q(r):X}");
    return 0;
}

// heapvectors <vtable rva hex> <min count>   vector headers anywhere in writable memory whose first and last items are objects of the class (read only)
int HeapVectorsCommand(uint vtableRva, int minCount)
{
    using var session = Attach(writable: false);
    var mem = session.Memory; ulong b = session.Game.ModuleBase;
    ulong Q(ulong at) { try { return mem.ReadUInt64(at); } catch { return 0; } }
    int found = 0;
    foreach (var region in session.Process.EnumerateWritableRegions(0x10000UL, 0x7FFF00000000UL))
        for (ulong off = 0; off < region.Size; off += 0x100000)
        {
            int n = (int)Math.Min(0x100000UL, region.Size - off);
            var buf = new byte[n];
            if (!mem.TryRead(region.Start + off, buf)) continue;
            for (int i = 0; i + 24 <= n; i += 8)
            {
                ulong begin = BitConverter.ToUInt64(buf, i), end = BitConverter.ToUInt64(buf, i + 8), cap = BitConverter.ToUInt64(buf, i + 16);
                if (begin < 0x10000 || end <= begin || cap < end || (end - begin) % 8 != 0 || (end - begin) / 8 < (ulong)minCount || (end - begin) / 8 > 5000) continue;
                if (Q(Q(begin)) != b + vtableRva || Q(Q(end - 8)) != b + vtableRva) continue;
                Console.WriteLine($"header at 0x{region.Start + off + (ulong)i:X}: {(end - begin) / 8} items, begin 0x{begin:X}");
                if (++found >= 10) return 0;
            }
        }
    Console.WriteLine($"{found} header(s)");
    return 0;
}
