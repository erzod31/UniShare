import { francAll } from 'franc-min';
// Keep detection independent of the installed model set; unsupported languages must stay unsupported.
const iso = {
  afr:'af',amh:'am',ara:'ar',arb:'ar',aze:'az',azj:'az',bel:'be',ben:'bn',bos:'bs',bul:'bg',cat:'ca',
  ces:'cs',cmn:'zh-Hans',dan:'da',deu:'de',ell:'el',eng:'en',est:'et',eus:'eu',fas:'fa',pes:'fa',fin:'fi',
  fra:'fr',glg:'gl',guj:'gu',heb:'he',hin:'hi',hrv:'hr',hun:'hu',ind:'id',isl:'is',ita:'it',jpn:'ja',
  kan:'kn',kor:'ko',lit:'lt',lav:'lv',lvs:'lv',mal:'ml',mar:'mr',mkd:'mk',nld:'nl',nno:'nb',nob:'nb',
  nor:'nb',por:'pt',ron:'ro',rus:'ru',slk:'sk',slv:'sl',spa:'es',sqi:'sq',als:'sq',srp:'sr',swe:'sv',
  tam:'ta',tel:'te',tgl:'tl',tha:'th',tur:'tr',uig:'ug',ukr:'uk',urd:'ur',vie:'vi',zlm:'ms',msa:'ms',
};
export function detectLanguage(text) {
  const clean = text.replace(/https?:\/\/[^\s<>"']+/giu, ' ').replace(/[#@][\p{L}\p{N}_]+/gu, ' ');
  if ((clean.match(/\p{L}/gu) || []).length < 4) return '';
  const candidates = francAll(clean, { minLength: 4 });
  return iso[candidates[0]?.[0]] || '';
}
