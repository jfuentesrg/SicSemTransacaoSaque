#region " Diretiva de Uso "
using System;
using System.Collections.Generic;
using System.Text;
using Sic.Unidade;
using Sic.Utilidade;
using Sic.Numerario;
using Sic.Evento;
using System.Threading;
using System.Data;
using System.Globalization;
#endregion

namespace SicSemTransacaoSaque {
    public class SemTransacao : IServico {
        private const int intervalo = 60000 * 10; //A cada 10 min.
        private Timer tmrAplicar;

        public SemTransacao() {
            tmrAplicar = new Timer(new TimerCallback(Verificar), this, 0, Timeout.Infinite);
        }
        private void Verificar(object state) {
            Log.Aviso("Iniciando a Verificação!");
            tmrAplicar.Change(Timeout.Infinite, Timeout.Infinite);
            Processar();
            tmrAplicar.Change(intervalo, Timeout.Infinite);
            Log.Aviso("Verificação Concluida!");
        }
        private void Processar() {
            //Carrega todos os equipamentos sem transação de saque das ultimas 48h
            using (DataTable TbEquipamentos = Sic.Numerario.Transacao.ListarSemTransacaoSaque(48)) {
                foreach (DataRow row in TbEquipamentos.Rows) {
                    int ponto = Convert.ToInt32(row["NR_SEQU_PONT_GRUP_EQPT"]);
                    int equipamento = Convert.ToInt32(row["CD_EQPT"]);
                    DateTime ultimoEvento = Convert.ToDateTime(row["TEMPO"]);
                    TimeSpan tempo = DateTime.Now.Subtract(ultimoEvento);

                    VerificarIntervalo(ponto, equipamento, tempo, ultimoEvento);
                }
            }
        }

        private bool VerificarIntervalo(int ponto, int equipamento, TimeSpan tempo, DateTime ultimoEvento) {
            DataTable dtDisponivel = Sic.Unidade.Equipamento.ListarHorarioCompleto(ponto, equipamento);
            TimeSpan dtAtual = new TimeSpan(DateTime.Now.Hour, DateTime.Now.Minute, 0);
            TimeSpan tsTempoTotalSemTransacao = TimeSpan.Zero;
            TimeSpan tsDiferencaHojeUltimoEvento = DateTime.Now - ultimoEvento;
            DateTime dtUltimoEvento = ultimoEvento;
            foreach (DataRow dr in dtDisponivel.Rows) {
                //Se não está no horário de atendimento do ponto, aborta;
                DataRow[] drHorarioAtual = dtDisponivel.Select("TP_DIA = " + DateTime.Now.DayOfWeek.ToDayOfSIC());
                if (drHorarioAtual.Length.Equals(0)) {
                    continue;
                }
                if (dtAtual.TotalMinutes < Int32.Parse(drHorarioAtual[0]["QT_MINU_INIC"].ToString()) ||
                    dtAtual.TotalMinutes > Int32.Parse(drHorarioAtual[0]["QT_MINU_FINA"].ToString())) {
                    return false;
                }
                while (tsDiferencaHojeUltimoEvento > TimeSpan.Zero) {
                    if (DateTime.Now.Subtract(ultimoEvento) > new TimeSpan(5, 0, 0, 0)) {
                        return false;
                    }
                    if (ultimoEvento > DateTime.Now) {
                        break;
                    }
                    TimeSpan tempoGasto = new TimeSpan();
                    DataRow[] drUltimoEvento = dtDisponivel.Select("TP_DIA = " + ultimoEvento.DayOfWeek.ToDayOfSIC());
                    if (!drUltimoEvento.Length.Equals(0)) {
                        if (ultimoEvento.Hour.Equals(0) && ultimoEvento.Minute.Equals(0) && !tsTempoTotalSemTransacao.TotalMinutes.Equals(TimeSpan.Zero)) {
                            ultimoEvento = ultimoEvento.AddMinutes(Int32.Parse(drHorarioAtual[0]["QT_MINU_INIC"].ToString()));
                        }
                        if (ultimoEvento.Date.Equals(DateTime.Now.Date) && DateTime.Now >= ultimoEvento) {
                            tempoGasto = new TimeSpan(0, (DateTime.Now.Hour * 60 + DateTime.Now.Minute) - (ultimoEvento.Hour * 60 + ultimoEvento.Minute), 0);
                        } else {
                            tempoGasto = new TimeSpan(0, Int32.Parse(drHorarioAtual[0]["QT_MINU_FINA"].ToString()) - (ultimoEvento.Hour * 60 + ultimoEvento.Minute), 0);
                        }
                        tsTempoTotalSemTransacao = tsTempoTotalSemTransacao.Add(tempoGasto);
                        tsDiferencaHojeUltimoEvento = tsDiferencaHojeUltimoEvento.Subtract(tempoGasto);
                    }
                    ultimoEvento = ultimoEvento.AddDays(1);
                    ultimoEvento = new DateTime(ultimoEvento.Year, ultimoEvento.Month, ultimoEvento.Day, 0, 0, 0);
                }
                break;
            }
            double totalHoras = tsTempoTotalSemTransacao.TotalHours;

            //MAIS de 2 DIAs sem transação financeira
            if (totalHoras >= 48) {
                return GerarEvento(ponto, equipamento, dtUltimoEvento);
            }

            return false;
        }

        /// <summary>
        /// Gera evento com componente hardcode (2800)
        /// </summary>
        /// <param name="ponto"></param>
        /// <param name="equipamento"></param>
        /// <param name="ultimoEvento"></param>
        /// <returns></returns>
        private bool GerarEvento(int ponto, int equipamento, DateTime ultimoEvento) {
            //Montando o Extra da mensagem;
            string extra = ultimoEvento.ToString();
            if (ultimoEvento == Convert.ToDateTime("1900-01-01 00:00:00")) {
                extra = "Sem Transação de saque";
            } else {
                extra = string.Format("Data Utima Transação: {0}", ultimoEvento.ToString("dd/MM/yyyy HH:mm:ss"));
            }

            //Gerando o evendo.
            EstruturaMaterializacao mat = Materializacao.Registrar(ponto, equipamento, 2800, 1500, 5, DateTime.Now, extra);
            return mat.ProcessadoOK;
        }

        #region IServico Members

        public void Atualizar() {
            if (tmrAplicar != null) {
                tmrAplicar.Dispose();
            }
            Verificar(this);
        }

        public void Continuar() {
            throw new Exception("The method or operation is not implemented.");
        }

        public void Parar() {
            throw new Exception("The method or operation is not implemented.");
        }

        #endregion

        #region IDisposable Members

        public void Dispose() {
            tmrAplicar.Dispose();
        }

        #endregion
    }
}