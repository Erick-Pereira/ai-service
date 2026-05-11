namespace Simcag.AIService.Domain.Services;

using Simcag.AIService.Domain.ValueObjects;

/// <summary>
/// Serviço de domínio para classificar um produto em uma categoria com base em regras (fallback).
/// </summary>
public interface ICategoryMatcher
{
    CategoryName MatchCategory(string productDescription);
}

/// <summary>
/// Implementação concreta do CategoryMatcher com keyword mapping.
/// </summary>
public sealed class CategoryMatcher : ICategoryMatcher
{
    private static readonly Dictionary<string, string> KeywordMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Notebooks
         { "notebook", "Notebook" },
        { "laptop", "Notebook" },
        { "macbook", "Notebook" },
        { "ultrabook", "Notebook" },
        // Monitores
        { "monitor", "Monitor" },
        { "display", "Monitor" },
        { "screen", "Monitor" },
        // Periféricos
        { "mouse", "Periférico" },
        { "teclado", "Periférico" },
        { "keyboard", "Periférico" },
        { "headset", "Periférico" },
        { "webcam", "Periférico" },
        { "web cam", "Periférico" },
        { "câmera", "Periférico" },
        { "camera", "Periférico" },
        // Hardware
        { "cpu", "Hardware" },
        { "processador", "Hardware" },
        { "processor", "Hardware" },
        { "gpu", "Hardware" },
        { "placa de vídeo", "Hardware" },
        { "video card", "Hardware" },
        { "memória", "Hardware" },
        { "memory", "Hardware" },
        { "ram", "Hardware" },
        { "ssd", "Hardware" },
        { "hdd", "Hardware" },
        { "disco", "Hardware" },
        { "hard disk", "Hardware" },
        { "placa-mãe", "Hardware" },
        { "motherboard", "Hardware" },
        // Software
        { "software", "Software" },
        { "licença", "Software" },
        { "license", "Software" },
        { "assinatura", "Software" },
        { "subscription", "Software" },
        { "windows", "Software" },
        { "office", "Software" },
        { "antivírus", "Software" },
        { "antivirus", "Software" },
    
        // SERVIÇOS CONDOMINIAIS 
        // Manutenção Predial
        { "reforma", "Manutenção" },
        { "pintura", "Manutenção" },
        { "elétrica", "Manutenção" },
        { "hidráulica", "Manutenção" },
        { "telhado", "Manutenção" },
        { "elevador", "Manutenção" },
        { "ar condicionado", "Manutenção" },
        { "extintor", "Manutenção" },
        // Conservação e Áreas Comuns
        { "limpeza", "Conservação" },
        { "faxina", "Conservação" },
        { "jardinagem", "Conservação" },
        { "piscina", "Conservação" },
        { "dedetização", "Conservação" },
        { "desentupimento", "Conservação" },
        // Segurança e Acesso
        { "segurança", "Segurança" },
        { "vigilância", "Segurança" },
        { "portaria", "Segurança" },
        { "monitoramento", "Segurança" },
        { "cftv", "Segurança" },
        { "interfone", "Segurança" },
        { "alarme", "Segurança" },
        // Administrativo e Jurídico
        { "contabilidade", "Administrativo" },
        { "jurídico", "Administrativo" },
        { "advocacia", "Administrativo" },
        { "seguro", "Administrativo" },
        { "vistoria", "Administrativo" },
        { "auditoria", "Administrativo" },
    
        // SUPRIMENTOS E CONSUMO (DIÁRIO)
        { "papel higiênico", "Suprimentos" },
        { "detergente", "Suprimentos" },
        { "desinfetante", "Suprimentos" },
        { "saco de lixo", "Suprimentos" },
        { "cloro", "Suprimentos" },
        { "lâmpada", "Suprimentos" },
    
        // INFRAESTRUTURA E EQUIPAMENTOS CRÍTICOS ---
        { "gerador", "Infraestrutura" },
        { "bomba d'água", "Infraestrutura" },
        { "caixa d'água", "Infraestrutura" },
        { "portão eletrônico", "Infraestrutura" },
        { "automação", "Infraestrutura" },
        { "antena", "Infraestrutura" },
        { "para-raios", "Infraestrutura" }, // SPDA (Obrigatório por lei)
    
        // SEGURANÇA CONTRA INCÊNDIO (OBRIGATÓRIO)
        { "avcb", "Segurança" },
        { "mangueira de incêndio", "Segurança" },
        { "hidrante", "Segurança" },
        { "iluminação de emergência", "Segurança" },
        { "porta corta-fogo", "Segurança" },
    
        // ÁREAS COMUNS E LAZER
        { "playground", "Lazer" },
        { "parquinho", "Lazer" },
        { "academia", "Lazer" },
        { "fitness", "Lazer" },
        { "brinquedoteca", "Lazer" },
        { "salão de festas", "Lazer" },
        { "churrasqueira", "Lazer" },
        { "mobiliário", "Lazer" },
    
        // GESTÃO DE RESÍDUOS E MEIO AMBIENTE
        { "coleta de lixo", "Serviços" },
        { "reciclagem", "Serviços" },
        { "podas", "Serviços" },
        { "controle de pragas", "Serviços" },
    
        // PESSOAL E RH (CASO O CONDOMÍNIO TENHA FUNCIONÁRIOS)
        { "uniforme", "RH" },
        { "epi", "RH" },
        { "vale transporte", "RH" },
        { "ticket", "RH" },
        { "exame admissional", "RH" },
    
        // COMUNICAÇÃO E TECNOLOGIA
        { "wi-fi", "Tecnologia" },
        { "roteador", "Tecnologia" },
        { "cabeamento estruturado", "Tecnologia" },
        { "aplicativo", "Tecnologia" },
        { "software de gestão", "Tecnologia" },
        { "totem", "Tecnologia" },
    
        // MANUTENÇÃO TÉCNICA E HIDRÁULICA
        { "barrilete", "Hidráulica" },
        { "coluna de esgoto", "Hidráulica" },
        { "impermeabilização", "Manutenção" },
        { "junta de dilatação", "Manutenção" },
        { "pastilha", "Manutenção" }, // Fachada
        { "lavagem de fachada", "Manutenção" },
        // ACESSIBILIDADE E SINALIZAÇÃO
        { "placa de sinalização", "Infraestrutura" },
        { "piso tátil", "Acessibilidade" },
        { "rampa", "Acessibilidade" },
        { "corrimão", "Acessibilidade" },
        { "plataforma elevatória", "Acessibilidade" },
    
        // GESTÃO DE ENERGIA E ÁGUA
        { "individualização de água", "Gestão" },
        { "leitura de gás", "Gestão" },
        { "energia solar", "Infraestrutura" },
        { "fotovoltaica", "Infraestrutura" },
        { "banco de capacitores", "Elétrica" },
    
        // EVENTOS E CONVENIÊNCIA
        { "decoração natalina", "Eventos" },
        { "buffet", "Eventos" },
        { "brinquedo inflável", "Eventos" },
        { "máquina de vendas", "Conveniência" },
        { "mercado autônomo", "Conveniência" },
    
        // TAXAS E OBRIGAÇÕES LEIAIS 
        { "iptu", "Taxas" },
        { "taxa de lixo", "Taxas" },
        { "foro", "Taxas" },
        { "laudêmio", "Taxas" },
        { "certificação digital", "Administrativo" },
    
        // SUSTENTABILIDADE E MEIO AMBIENTE
        { "reuso de água", "Sustentabilidade" },
        { "coleta seletiva", "Sustentabilidade" },
        { "composteira", "Sustentabilidade" },
        { "lâmpada led", "Sustentabilidade" },
        { "sensor de presença", "Sustentabilidade" },
    
        // GARAGEM E VEÍCULOS 
        { "vaga de garagem", "Infraestrutura" },
        { "carregador elétrico", "Infraestrutura" },
        { "estacionamento", "Infraestrutura" },
        { "protetor de coluna", "Infraestrutura" },
        { "espelho convexo", "Segurança" },
        { "semáforo", "Segurança" },
    
        // SEGURANÇA ELETRÔNICA AVANÇADA 
        { "biometria", "Segurança" },
        { "reconhecimento facial", "Segurança" },
        { "tag", "Segurança" },
        { "eletroímã", "Segurança" },
        { "mola hidráulica", "Manutenção" },
        { "concertina", "Segurança" },
        { "cerca elétrica", "Segurança" },
    
        // SERVIÇOS PROFISSIONAIS E TAXAS
        { "sindicância", "Administrativo" },
        { "honorários", "Administrativo" },
        { "perícia", "Administrativo" },
        { "avaliação técnica", "Administrativo" },
        { "certidão", "Administrativo" },
        { "escritura", "Administrativo" },
    
        // MANUTENÇÃO DE ÁREAS SOCIAIS
        { "estofamento", "Manutenção" },
        { "limpeza de tapetes", "Conservação" },
        { "dedetização de bueiros", "Conservação" },
        { "limpeza de caixa de gordura", "Conservação" },
        { "desentupimento de prumada", "Hidráulica" },
    
        // BEM-ESTAR E SAÚDE (ÁREAS COMUNS)
        { "climatização", "Manutenção" },
        { "bebedouro", "Suprimentos" },
        { "purificador", "Suprimentos" },
        { "primeiros socorros", "Segurança" },
        { "desfibrilador", "Segurança" },
    
        // MATERIAIS DE LIMPEZA E HIGIENE (PRODUTOS)
        { "água sanitária", "Produtos de Limpeza" },
        { "multiuso", "Produtos de Limpeza" },
        { "álcool em gel", "Produtos de Limpeza" },
        { "limpa pedras", "Produtos de Limpeza" },
        { "removedor", "Produtos de Limpeza" },
        { "sabão líquido", "Produtos de Limpeza" },
        { "papel toalha", "Suprimentos" },
        { "dispenser", "Suprimentos" },
        { "mop", "Utensílios" },
        { "vassoura", "Utensílios" },
        { "pá de lixo", "Utensílios" },
    
        // MATERIAIS ELÉTRICOS (PRODUTOS)
        { "disjuntor", "Material Elétrico" },
        { "fio flexível", "Material Elétrico" },
        { "cabo de rede", "Material Elétrico" },
        { "tomada", "Material Elétrico" },
        { "interruptor", "Material Elétrico" },
        { "fita isolante", "Material Elétrico" },
        { "reator", "Material Elétrico" },
        { "soquete", "Material Elétrico" },
        { "estabilizador", "Material Elétrico" },
    
        // MATERIAIS HIDRÁULICOS (PRODUTOS)
        { "reparo de descarga", "Material Hidráulico" },
        { "torneira", "Material Hidráulico" },
        { "sifão", "Material Hidráulico" },
        { "vazamento", "Material Hidráulico" },
        { "tubo pvc", "Material Hidráulico" },
        { "conexão", "Material Hidráulico" },
        { "registro", "Material Hidráulico" },
        { "boia de caixa d'água", "Material Hidráulico" },
        { "veda rosca", "Material Hidráulico" },
    
        // FERRAGENS E FERRAMENTAS (PRODUTOS)
        { "parafuso", "Ferragens" },
        { "bucha", "Ferragens" },
        { "prego", "Ferragens" },
        { "dobradiça", "Ferragens" },
        { "cadeado", "Ferragens" },
        { "fechadura", "Ferragens" },
        { "silicone", "Ferragens" },
        { "massa plástica", "Ferragens" },
        { "furadeira", "Ferramentas" },
        { "alicates", "Ferramentas" },
        { "chave de fenda", "Ferramentas" },
    
        // PINTURA E REFORMA (PRODUTOS)
        { "tinta acrílica", "Pintura" },
        { "esmalte sintético", "Pintura" },
        { "rolo de pintura", "Pintura" },
        { "pincel", "Pintura" },
        { "solvente", "Pintura" },
        { "massa corrida", "Pintura" },
        { "lixa", "Pintura" },
    
        // JARDINAGEM (PRODUTOS)
        { "adubo", "Jardinagem" },
        { "terra vegetal", "Jardinagem" },
        { "muda", "Jardinagem" },
        { "grama", "Jardinagem" },
        { "mangueira", "Jardinagem" },
        { "aspersor", "Jardinagem" },
        { "tesoura de poda", "Jardinagem" },
    
        // SEGURANÇA E SINALIZAÇÃO (PRODUTOS)
        { "cone de sinalização", "Segurança" },
        { "corrente plástica", "Segurança" },
        { "fita antiderrapante", "Segurança" },
        { "extintor pó químico", "Segurança" },
        { "placa 'piso molhado'", "Segurança" },
        { "lixeira", "Infraestrutura" },
        { "contentor", "Infraestrutura" }
    };

    public CategoryName MatchCategory(string productDescription)
    {
        foreach (var (keyword, category) in KeywordMap)
        {
            if (productDescription.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return new CategoryName(category);
        }

        return new CategoryName("Outro");
    }
}
