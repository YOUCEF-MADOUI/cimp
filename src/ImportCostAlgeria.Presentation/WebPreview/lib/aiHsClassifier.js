'use strict';
/**
 * Classificateur SH par IA (port fonctionnel de HSClassifierService — Section 16).
 * INTERDICTION ABSOLUE : ne modifie jamais automatiquement le Code SH confirmé.
 * Retourne uniquement une PROPOSITION que l'utilisateur doit explicitement CONFIRMER ou MODIFIER.
 */

const KNOWLEDGE_BASE = [
  {
    keywords: ['ENGINE MOUNTING', 'SUPPORT MOTEUR', 'SILENTBLOC MOTEUR', 'MOUNTING'],
    hsCode10: '8708.99.90.00',
    tariffDescFr: "Autres parties et accessoires des véhicules automobiles des n°s 87.01 à 87.05",
    confidence: 87.0,
    rgi: 'RGI 1 et RGI 6 (Section XVII, Chapitre 87, Note 2 et 3)',
    justification: "Pièce mécanique antivibratoire identifiable comme étant exclusivement ou principalement destinée aux véhicules automobiles du Chapitre 87."
  },
  {
    keywords: ['DURITE', 'HOSE', 'FLEXIBLE HOSE', 'RADIATOR HOSE'],
    hsCode10: '4009.42.00.00',
    tariffDescFr: 'Tubes et tuyaux en caoutchouc vulcanisé non durci, avec accessoires, renforcés',
    confidence: 82.0,
    rgi: 'RGI 1 (Section VII, Chapitre 40, Position 40.09)',
    justification: 'Article en caoutchouc souple de type tuyau/durite relevant de la position 40.09 selon sa composition et son usage.'
  },
  {
    keywords: ['FILTRE HYDRAULIQUE', 'HYDRAULIC FILTER', 'FILTRE HUILE', 'OIL FILTER'],
    hsCode10: '8421.29.90.00',
    tariffDescFr: 'Appareils pour la filtration ou l\'épuration des liquides — Autres',
    confidence: 91.5,
    rgi: 'RGI 1 et RGI 6 (Section XVI, Chapitre 84, Position 84.21)',
    justification: 'Appareil de filtration pour fluides hydrauliques industriels relevant spécifiquement de la position 84.21.'
  },
  {
    keywords: ['ROULEMENT A BILLES', 'BALL BEARING', 'ROULEMENT'],
    hsCode10: '8482.10.00.00',
    tariffDescFr: 'Roulements à billes',
    confidence: 94.0,
    rgi: 'RGI 1 (Section XVI, Chapitre 84, Position 84.82)',
    justification: 'Les roulements à billes sont dénommés spécifiquement à la sous-position 8482.10 indépendamment de la machine de destination (Note 2 Section XVI).'
  }
];

function proposeHsCode(reference, designation, description) {
  const haystack = `${reference} ${designation} ${description || ''}`.toUpperCase();
  for (const entry of KNOWLEDGE_BASE) {
    if (entry.keywords.some(k => haystack.includes(k))) {
      return {
        proposedHsCode10: entry.hsCode10,
        tariffDescriptionFr: entry.tariffDescFr,
        confidencePercent: entry.confidence,
        generalInterpretiveRuleUsed: entry.rgi,
        justificationFr: entry.justification,
        dataTag: 'PROPOSITION_IA',
        status: 'PROPOSE_IA_NON_CONFIRME'
      };
    }
  }
  return {
    proposedHsCode10: 'INFORMATION NON DÉTERMINÉE',
    tariffDescriptionFr: "Description technique insuffisante pour proposer une sous-position à 10 chiffres avec certitude.",
    confidencePercent: 0,
    generalInterpretiveRuleUsed: 'RGI 1 — Information complémentaire requise',
    justificationFr: "Aucun code SH n'est inventé en l'absence d'éléments techniques suffisants (Section 41). Saisie manuelle requise.",
    dataTag: 'PROPOSITION_IA',
    status: 'NON_DETERMINE'
  };
}

module.exports = { proposeHsCode };
